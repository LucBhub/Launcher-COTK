using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace COTK.Next.Core;

/// <summary>Installation, mise à jour différentielle et réparation du client du jeu.
///
/// Garanties (par rapport à l'ancien launcher) :
///  • chaque fichier est téléchargé dans « .cotk-part » (reprise Range) puis renommé atomiquement
///    APRÈS vérification SHA-256 : jamais de fichier tronqué sous son vrai nom ;
///  • les fichiers « disparus du catalogue » ne sont supprimés qu'APRÈS le succès du téléchargement ;
///  • l'échec du delta ne relance JAMAIS 15 Go en silence : l'utilisateur confirme le repli ;
///  • espace disque contrôlé avant d'écrire ; progression en octets réels, vitesse et temps restant ;
///  • cache d'empreintes : la vérification d'un client intact est quasi instantanée.</summary>
internal sealed class ClientInstaller
{
    private const string MarkerName = ".cotk-client-version";
    private const int DeltaParallelism = 4;
    private const long DiskMargin = 256L << 20;

    private static readonly HttpClient Download = CreateDownloadClient();

    /// <summary>Confirme un téléchargement lourd. (octets, estUneMiseÀJour) → vrai pour continuer.</summary>
    public Func<long, bool, Task<bool>>? ConfirmFullDownload { get; set; }
    public Func<StoragePlan,bool,Task<bool>>? ConfirmStoragePlan { get; set; }
    public TransferPause TransferControl { get; }=new();

    /// <summary>Demande où installer (première installation). Null = annulé.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    private static HttpClient CreateDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 8,
            AllowAutoRedirect = false,
        };
        var http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"COTK-Launcher/{Config.Version}");
        return http;
    }

    // ------------------------------------------------------------------ état

    public static string ClientDir => Settings.ClientDirectory ?? Path.Combine(AppPaths.InstallDir, "client");

    private static string MarkerPath => Path.Combine(ClientDir, MarkerName);

    public static string? InstalledVersion()
    {
        try { return File.Exists(MarkerPath) ? File.ReadAllText(MarkerPath).Trim() : null; }
        catch { return null; }
    }

    public static bool ExeExists(ClientRelease release) => File.Exists(Path.Combine(ClientDir, release.Executable));

    public static bool IsInstalled(ClientRelease release) =>
        ExeExists(release) && string.Equals(InstalledVersion(), release.Version, StringComparison.Ordinal);

    public static bool NeedsUpdate(ClientRelease release) => ExeExists(release) && !IsInstalled(release);

    public async Task<string[]> GetUninstallCatalogueAsync(ClientRelease release, CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(release.FilesUrl)) throw new LauncherException(new LauncherError("E-UNINSTALL-002","Liste de fichiers indisponible","Impossible d'identifier les fichiers du client. Connectez-vous pour récupérer son catalogue puis réessayez."));
        ManifestRules.ValidateUrl(release.FilesUrl);
        var catalog=await FetchCatalogAsync(release.FilesUrl,ct,release.FilesSha256);
        if(catalog.SchemaVersion!=1 || catalog.Version!=release.Version || catalog.Files.Count!=catalog.FileCount || catalog.Files.Any(f=>!ManifestRules.IsSafeRelative(f.Path)))
            throw new LauncherException(Errors.UnsafeUpdate("Le catalogue de fichiers ne correspond pas à la version annoncée."));
        return catalog.Files.Select(f=>f.Path).ToArray();
    }

    // ------------------------------------------------------------------ point d'entrée

    public async Task<ClientSyncResult> EnsureAsync(ClientRelease release, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (IsInstalled(release)) return ClientSyncResult.AlreadyCurrent;

        var isUpdate = ExeExists(release);
        var fullApproved = false;
        if (string.IsNullOrWhiteSpace(release.ArchiveUrl))
        {
            if (!isUpdate) throw new LauncherException(Errors.Manifest("aucune archive publiée et client absent"));
            await File.WriteAllTextAsync(MarkerPath, release.Version + Environment.NewLine, ct);
            return new ClientSyncResult(true, false, 0, 0, 0);
        }

        if (isUpdate && !string.IsNullOrWhiteSpace(release.FilesUrl))
        {
            try
            {
                var delta = await SyncDeltaAsync(release, trustCache: true, removeStale: true, progress, ct);
                await File.WriteAllTextAsync(MarkerPath, release.Version + Environment.NewLine, ct);
                Log.Info($"mise à jour différentielle terminée : {release.Version} ({delta.FilesChecked} vérifiés, {delta.FilesDownloaded} téléchargés, {delta.BytesDownloaded} o)");
                progress.Report(new TransferProgress(TransferPhase.Done, $"Client {release.Version} à jour."));
                return delta;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not UserDeclinedException)
            {
                var error = Errors.From(ex);
                if (error.Code is "E-UPD-004" or "E-DISK-001") throw;
                Log.Warn($"mise à jour différentielle échouée ({error.Code}), repli sur l'archive complète possible", ex);

                // Pas de repli silencieux : on dit pourquoi et on demande avant de retélécharger tout le client.
                if (ConfirmStoragePlan is null && (ConfirmFullDownload is null || !await ConfirmFullDownload(release.SizeBytes, true)))
                    throw;
                fullApproved = ConfirmStoragePlan is null;
            }
        }
        else if (isUpdate && ConfirmFullDownload is not null)
        {
            if (!await ConfirmFullDownload(release.SizeBytes, true)) throw new UserDeclinedException();
            fullApproved = true;
        }

        return await InstallFullArchiveAsync(release, isUpdate, progress, ct, fullApproved);
    }

    /// <summary>Compare chaque fichier au catalogue. <paramref name="repair"/> ignore le cache d'empreintes
    /// (relit tout le disque) ; sinon la vérification est quasi instantanée si rien n'a changé.</summary>
    public async Task<ClientSyncResult> VerifyAsync(ClientRelease release, bool repair, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(release.FilesUrl))
            throw new LauncherException(Errors.Manifest("pas de catalogue de fichiers publié"));
        if (!ExeExists(release))
            throw new LauncherException(Errors.NotInstalled());

        var result = await SyncDeltaAsync(release, trustCache: !repair, removeStale: true, progress, ct);
        await File.WriteAllTextAsync(MarkerPath, release.Version + Environment.NewLine, ct);
        progress.Report(new TransferProgress(TransferPhase.Done, "Vérification terminée."));
        return result;
    }

    // ------------------------------------------------------------------ delta

    private async Task<ClientSyncResult> SyncDeltaAsync(
        ClientRelease release, bool trustCache, bool removeStale, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        var filesUrl = release.FilesUrl ?? throw new LauncherException(Errors.Manifest("catalogue absent"));
        ManifestRules.ValidateUrl(filesUrl);

        progress.Report(new TransferProgress(TransferPhase.Preparing, "Lecture du catalogue de fichiers…"));
        var catalog = await FetchCatalogAsync(filesUrl, ct,release.FilesSha256);
        if (catalog.SchemaVersion != 1 || catalog.Version != release.Version || catalog.FileCount != catalog.Files.Count
            || catalog.Files.Any(f => !ManifestRules.IsSafeRelative(f.Path) || f.Size < 0 || f.Sha256 is not { Length: 64 } || !f.Sha256.All(Uri.IsHexDigit))
            || catalog.TotalBytes != catalog.Files.Sum(f => f.Size)
            || catalog.Files.Select(f => f.Path.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Files.Count)
            throw new LauncherException(Errors.UnsafeUpdate("Le catalogue de fichiers ne correspond pas à la version annoncée."));
        var clientDir = ClientDir;
        Directory.CreateDirectory(clientDir);
        var baseUrl = !string.IsNullOrWhiteSpace(catalog.BaseUrl)
            ? catalog.BaseUrl.TrimEnd('/') + "/"
            : filesUrl[..(filesUrl.LastIndexOf('/') + 1)] + "client/";
        ManifestRules.ValidateUrl(baseUrl);

        var cache = HashCache.Load(clientDir);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toDownload = new List<ClientFileEntry>();

        // ---- analyse : taille puis (cache | SHA-256) ----
        var totalBytes = Math.Max(1L, catalog.Files.Sum(f => f.Size));
        long analysed = 0;
        var analysedFiles = 0;
        var lastReport = 0L;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var entry in catalog.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!ManifestRules.IsSafeRelative(entry.Path) || entry.Sha256 is not { Length: 64 } || entry.Size < 0)
                throw new LauncherException(Errors.UnsafeUpdate($"Entrée de catalogue invalide : {entry.Path}"));
            known.Add(entry.Path.Replace('\\', '/'));

            var local = LocalPath(clientDir, entry.Path);
            var info = new FileInfo(local);
            var keep = false;
            if (ManifestRules.IsPlayerData(entry.Path) && info.Exists)
            {
                keep = true;
            }
            else if (info.Exists && info.Length == entry.Size)
            {
                if (trustCache && cache.TryGet(entry.Path, info, out var cached)
                    && string.Equals(cached, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    keep = true;
                }
                else
                {
                    var baseline = analysed;
                    var sha = await Hashing.Sha256Async(local, ct, read =>
                    {
                        analysed += read;
                        if (watch.ElapsedMilliseconds - lastReport > 120)
                        {
                            lastReport = watch.ElapsedMilliseconds;
                            progress.Report(new TransferProgress(TransferPhase.Analyzing,
                                $"Analyse des fichiers… {analysedFiles}/{catalog.Files.Count}", analysed, totalBytes, 0, null, analysedFiles, catalog.Files.Count));
                        }
                    });
                    analysed = baseline;
                    if (string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        cache.Set(entry.Path, info, sha);
                        keep = true;
                    }
                    else cache.Forget(entry.Path);
                }
            }

            if (!keep) toDownload.Add(entry);
            analysed += entry.Size;
            analysedFiles++;
            if (watch.ElapsedMilliseconds - lastReport > 120)
            {
                lastReport = watch.ElapsedMilliseconds;
                progress.Report(new TransferProgress(TransferPhase.Analyzing,
                    $"Analyse des fichiers… {analysedFiles}/{catalog.Files.Count}", analysed, totalBytes, 0, null, analysedFiles, catalog.Files.Count));
            }
        }
        cache.Save();

        var downloadBytes = toDownload.Sum(f => f.Size);
        Log.Info($"delta : {toDownload.Count}/{catalog.Files.Count} fichier(s) à mettre à jour ({downloadBytes} o)");

        if (toDownload.Count > 0)
        {
            EnsureDiskSpace(clientDir, downloadBytes + DiskMargin);
            await DownloadFilesAsync(toDownload, baseUrl, clientDir, downloadBytes, cache, progress, ct,TransferControl);
        }
        ct.ThrowIfCancellationRequested();

        // Suppression des fichiers disparus : SEULEMENT après le succès de tout le reste.
        if (removeStale)
        {
            progress.Report(new TransferProgress(TransferPhase.Cleaning, "Nettoyage des fichiers obsolètes…"));
            RemoveStale(clientDir, known);
        }
        cache.Save();
        var inventory = Path.Combine(clientDir, ".cotk-managed-files.json");
        await File.WriteAllTextAsync(inventory + ".tmp", JsonSerializer.Serialize(known), ct);
        File.Move(inventory + ".tmp", inventory, overwrite: true);
        return new ClientSyncResult(true, false, catalog.Files.Count, toDownload.Count, downloadBytes);
    }

    private static async Task<ClientFileCatalog> FetchCatalogAsync(string url, CancellationToken ct,string? expectedSha=null)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(60));
            var bytes = await Download.GetByteArrayAsync(url, linked.Token);
            if(bytes.Length>8*1024*1024) throw new LauncherException(Errors.UnsafeUpdate("Catalogue trop volumineux."));
            if(expectedSha is not null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedSha,StringComparison.OrdinalIgnoreCase))
                throw new LauncherException(Errors.UnsafeUpdate("L'empreinte du catalogue ne correspond pas à la publication signée."));
            var catalog = JsonSerializer.Deserialize<ClientFileCatalog>(bytes, ApiClient.Json);
            if (catalog?.Files is null || catalog.Files.Count == 0)
                throw new LauncherException(Errors.Manifest("catalogue de fichiers vide"));
            return catalog;
        }
        catch (JsonException ex)
        {
            throw new LauncherException(Errors.Manifest("catalogue illisible: " + ex.Message), ex);
        }
    }

    private static async Task DownloadFilesAsync(
        List<ClientFileEntry> files, string baseUrl, string clientDir, long totalBytes,
        HashCache cache, IProgress<TransferProgress> progress, CancellationToken ct,TransferPause pause)
    {
        long done = 0;
        var filesDone = 0;
        var throughput = new Throughput();
        var gate = new object();
        var received = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var lastReport = 0L;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        void Report(string path, long absolute, bool fileFinished)
        {
            lock (gate)
            {
                received.TryGetValue(path, out var previous);
                done += absolute - previous;
                received[path] = absolute;
                if (fileFinished) filesDone++;
                if (!fileFinished && watch.ElapsedMilliseconds - lastReport < 120) return;
                lastReport = watch.ElapsedMilliseconds;
                throughput.Add(done);
                progress.Report(new TransferProgress(TransferPhase.Downloading,
                    $"Téléchargement… {filesDone}/{files.Count} fichiers",
                    done, totalBytes, throughput.BytesPerSecond, throughput.Eta(totalBytes - done), filesDone, files.Count));
            }
        }

        // Les plus gros d'abord : la queue de téléchargement se termine sur des petits fichiers.
        var ordered = files.OrderByDescending(f => f.Size).ToList();
        await Parallel.ForEachAsync(ordered,
            new ParallelOptions { MaxDegreeOfParallelism = DeltaParallelism, CancellationToken = ct },
            async (entry, token) =>
            {
                await DownloadOneFileAsync(baseUrl, entry, clientDir, cache, d => Report(entry.Path, d, false), token,pause);
                Report(entry.Path, entry.Size, true);
            });
    }

    private static async Task DownloadOneFileAsync(
        string baseUrl, ClientFileEntry entry, string clientDir, HashCache cache, Action<long> onBytes, CancellationToken ct,TransferPause pause)
    {
        var local = LocalPath(clientDir, entry.Path);
        var part = local + ".cotk-part";
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        var url = baseUrl + string.Join('/', entry.Path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await FetchToPartAsync(url, part, entry, onBytes, ct,pause);
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new LauncherException(Errors.RemoteFileMissing(entry.Path), ex);
            }
            catch (Exception ex) when (attempt < 4 && !ct.IsCancellationRequested
                                       && (ex is IOException or HttpRequestException or TimeoutException
                                           || (ex is LauncherException le && le.Error.Code == "E-UPD-002")))
            {
                Log.Warn($"téléchargement de {entry.Path} interrompu (essai {attempt}/4)", ex);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }

        ct.ThrowIfCancellationRequested();
        MoveIntoPlace(part, local);
        cache.Set(entry.Path, new FileInfo(local), entry.Sha256);
    }

    private static async Task FetchToPartAsync(string url, string part, ClientFileEntry entry, Action<long> onBytes, CancellationToken ct,TransferPause pause)
    {
        long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (offset > entry.Size) { File.Delete(part); offset = 0; }
        onBytes(offset);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (offset > 0)
        {
            // Reprise : l'empreinte doit couvrir les octets déjà reçus.
            await using var existing = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
            var buf = new byte[1 << 20];
            int n;
            while ((n = await existing.ReadAsync(buf, ct)) > 0) hash.AppendData(buf, 0, n);
        }

        if (offset < entry.Size || entry.Size == 0)
        {
            await pause.WaitAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            using var response = await Download.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                File.Delete(part);
                throw new HttpRequestException("Reprise obsolète, fichier repris à zéro.", null, HttpStatusCode.RequestedRangeNotSatisfiable);
            }
            response.EnsureSuccessStatusCode();

            if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                // Le serveur a ignoré Range : on repart de zéro.
                offset = 0;
                onBytes(0);
                hash.GetHashAndReset();
            }

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(part, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
            var buffer = new byte[1 << 17];
            int read;
            long received = offset;
            while (true)
            {
                await pause.WaitAsync(ct);
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(TimeSpan.FromMinutes(2));
                try { read = await input.ReadAsync(buffer, stall.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("Aucune donnée reçue depuis 2 minutes.");
                }
                if (read == 0) break;
                received += read;
                if (received > entry.Size) throw new LauncherException(Errors.HashMismatch(entry.Path));
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                onBytes(received);
            }
            await output.FlushAsync(ct);
        }

        var finalLength = new FileInfo(part).Length;
        if (finalLength < entry.Size) throw new HttpRequestException("Flux interrompu : le fichier partiel est conservé pour la reprise.");
        var sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (finalLength != entry.Size || !string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new LauncherException(Errors.HashMismatch(entry.Path));
        }
    }

    private static void MoveIntoPlace(string part, string local)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(part, local, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                // Antivirus qui scanne le fichier tout juste écrit.
                Thread.Sleep(400 * attempt);
            }
        }
    }

    private static void RemoveStale(string clientDir, HashSet<string> known)
    {
        var inventory = Path.Combine(clientDir, ".cotk-managed-files.json");
        if (!File.Exists(inventory)) return; // première migration : aucun fichier inconnu du joueur n'est supprimé
        var previous = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(inventory)) ?? new();
        foreach (var rel in previous.Where(ManifestRules.IsSafeRelative))
        {
            if (known.Contains(rel) || ManifestRules.IsPlayerData(rel)) continue;
            var file = LocalPath(clientDir, rel);
            try
            {
                File.Delete(file);
                Log.Info($"fichier obsolète supprimé : {rel}");
            }
            catch (Exception ex) { Log.Warn($"suppression impossible : {rel}", ex); }
        }

        foreach (var dir in Directory.EnumerateDirectories(clientDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch { }
        }
    }

    // ------------------------------------------------------------------ archive complète

    private async Task<ClientSyncResult> InstallFullArchiveAsync(
        ClientRelease release, bool isUpdate, IProgress<TransferProgress> progress, CancellationToken ct, bool approved)
    {
        ManifestRules.ValidateUrl(release.ArchiveUrl);

        if (!isUpdate && PickFolder is not null && Settings.ClientDirectory is null)
        {
            var chosen = await PickFolder();
            if (string.IsNullOrWhiteSpace(chosen)) throw new UserDeclinedException();
            // Le joueur désigne le dossier PARENT ; on crée « client » dedans pour ne jamais
            // déplacer ni renommer un dossier qui lui appartient (ex. D:\Games).
            Settings.ClientDirectory = Path.Combine(chosen, "client");
        }

        var clientDir = ClientDir;
        if (clientDir.Length > 150) throw new LauncherException(Errors.PathTooLong(clientDir));
        if (Directory.Exists(clientDir)
            && Directory.EnumerateFileSystemEntries(clientDir).Any()
            && !File.Exists(Path.Combine(clientDir, release.Executable))
            && !ClientUninstaller.CanReinstall(clientDir))
            throw new LauncherException(Errors.UnsafeFolder(clientDir));

        // The first budget uses the uncompressed catalogue if available; the ZIP is checked again before extraction.
        var downloadDir = Path.GetDirectoryName(clientDir.TrimEnd(Path.DirectorySeparatorChar)) ?? clientDir;
        long expanded=release.ExpandedSizeBytes>0 ? release.ExpandedSizeBytes : release.SizeBytes; var estimated=release.ExpandedSizeBytes<=0;
        if(!string.IsNullOrWhiteSpace(release.FilesUrl))
        {
            ManifestRules.ValidateUrl(release.FilesUrl);
            try
            {
                var catalog=await FetchCatalogAsync(release.FilesUrl,ct,release.FilesSha256);
                if(catalog.SchemaVersion!=1 || catalog.Version!=release.Version || catalog.TotalBytes!=catalog.Files.Sum(f=>f.Size))
                    throw new LauncherException(Errors.UnsafeUpdate("Le catalogue de fichiers ne correspond pas à la version annoncée."));
                expanded=catalog.TotalBytes; estimated=false;
            }
            catch(Exception ex) when(ex is HttpRequestException or TimeoutException) { Log.Warn("catalogue indisponible pour le budget disque : estimation avant téléchargement",ex); }
        }
        var saved=StorageCheck.SavedDataBytes(clientDir);
        var part=Path.Combine(downloadDir,$"cotk-client-{release.Sha256[..12].ToLowerInvariant()}.part");
        var resumed=File.Exists(part)?new FileInfo(part).Length:0;
        var required=StorageCheck.Required(release.SizeBytes,expanded,saved,resumed);
        var available=StorageCheck.FreeBytes(downloadDir);
        StorageCheck.Ensure(downloadDir,required);
        StorageCheck.CheckWritable(clientDir);
        var plan=new StoragePlan(clientDir,release.SizeBytes,expanded,saved,required,available,estimated);
        if(ConfirmStoragePlan is not null && !await ConfirmStoragePlan(plan,isUpdate)) throw new UserDeclinedException();
        if(ConfirmStoragePlan is null && !approved && ConfirmFullDownload is not null && !await ConfirmFullDownload(release.SizeBytes,isUpdate)) throw new UserDeclinedException();
        Directory.CreateDirectory(downloadDir);
        EnsureDiskSpace(downloadDir,required);

        var archive = await DownloadVerifiedAsync(release.ArchiveUrl, release.Sha256, release.SizeBytes, "client", downloadDir, progress, ct);
        EnsureDiskSpace(downloadDir,checked(StorageCheck.ExpandedZipBytes(archive)+saved+DiskMargin));
        await ExtractAndSwapAsync(archive, release, clientDir, progress, ct);
        return new ClientSyncResult(isUpdate, true, 0, 0, release.SizeBytes);
    }

    internal async Task<string> DownloadVerifiedAsync(
        string url, string expectedSha256, long expectedSize, string name, string dir,
        IProgress<TransferProgress> progress, CancellationToken ct)
    {
        ManifestRules.ValidateUrl(url);
        Directory.CreateDirectory(dir);
        // Nom déterministe (même que l'ancien launcher) : un .part commencé est repris.
        var path = Path.Combine(dir, $"cotk-{name}-{expectedSha256[..12].ToLowerInvariant()}.part");

        const int maxAttempts = 6;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await DownloadArchiveOnceAsync(url, path, expectedSize, progress, ct,TransferControl);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested
                                       && ex is HttpRequestException or IOException or TimeoutException)
            {
                var wait = TimeSpan.FromSeconds(2 * attempt);
                Log.Warn($"téléchargement {name} interrompu (essai {attempt}/{maxAttempts}), reprise dans {wait.TotalSeconds:0}s", ex);
                progress.Report(new TransferProgress(TransferPhase.Downloading,
                    $"Connexion interrompue — reprise dans {wait.TotalSeconds:0} s ({attempt}/{maxAttempts})", new FileInfo(path).Exists ? new FileInfo(path).Length : 0, expectedSize));
                await Task.Delay(wait, ct);
            }
        }

        // Vérification en flux, avec progression (15 Go ≈ plusieurs minutes : jamais une barre figée).
        long hashed = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var last = 0L;
        var sha = await Hashing.Sha256Async(path, ct, read =>
        {
            hashed += read;
            if (watch.ElapsedMilliseconds - last > 150)
            {
                last = watch.ElapsedMilliseconds;
                progress.Report(new TransferProgress(TransferPhase.Verifying, "Vérification de l'intégrité…", hashed, expectedSize));
            }
        });
        if (!string.Equals(sha, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new LauncherException(Errors.HashMismatch($"archive {name}"));
        }
        return path;
    }

    private static async Task DownloadArchiveOnceAsync(
        string url, string path, long expectedSize, IProgress<TransferProgress> progress, CancellationToken ct,TransferPause pause)
    {
        long offset = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (offset > expectedSize) { File.Delete(path); offset = 0; }
        if (expectedSize > 0 && offset == expectedSize) return; // déjà complet : la vérification tranchera

        await pause.WaitAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await Download.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (offset > 0)
            {
                File.Delete(path);
                throw new HttpRequestException("Téléchargement partiel obsolète, reprise à zéro.");
            }
            return;
        }
        if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent) offset = 0;
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long length && expectedSize > 0 && offset + length != expectedSize)
            throw new LauncherException(Errors.UnsafeUpdate(
                $"L'archive publiée ({Fmt.Size(offset + length)}) ne correspond pas au manifeste ({Fmt.Size(expectedSize)})."));

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(path, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
        var buffer = new byte[1 << 18];
        long total = offset;
        var throughput = new Throughput();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var last = 0L;

        while (true)
        {
            await pause.WaitAsync(ct);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(TimeSpan.FromMinutes(3));
            int read;
            try { read = await input.ReadAsync(buffer, stall.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Aucune donnée reçue depuis 3 minutes.");
            }
            if (read == 0) break;
            total += read;
            if (expectedSize > 0 && total > expectedSize)
                throw new LauncherException(Errors.UnsafeUpdate("L'archive reçue est plus grosse qu'annoncé."));
            await output.WriteAsync(buffer.AsMemory(0, read), ct);

            if (watch.ElapsedMilliseconds - last >= 150)
            {
                last = watch.ElapsedMilliseconds;
                throughput.Add(total);
                progress.Report(new TransferProgress(TransferPhase.Downloading, "Téléchargement du client…",
                    total, expectedSize, throughput.BytesPerSecond, throughput.Eta(expectedSize - total)));
            }
        }
        await output.FlushAsync(ct);
        if (expectedSize > 0 && total != expectedSize)
            throw new HttpRequestException($"Flux terminé prématurément ({total}/{expectedSize} octets).");
    }

    private static async Task ExtractAndSwapAsync(
        string archive, ClientRelease release, string clientDir, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        var trimmed = clientDir.TrimEnd(Path.DirectorySeparatorChar);
        var stage = trimmed + ".update-" + Guid.NewGuid().ToString("N");
        var backup = trimmed + ".backup-" + Guid.NewGuid().ToString("N");
        var installed = false;
        try
        {
            await Task.Run(() => ExtractZipSafely(archive, stage, progress, ct), ct);
            if (!File.Exists(Path.Combine(stage, release.Executable)))
                throw new LauncherException(Errors.UnsafeUpdate($"L'archive ne contient pas {release.Executable} à sa racine."));

            progress.Report(new TransferProgress(TransferPhase.Installing, "Installation…"));
            var owned=Directory.EnumerateFiles(stage,"*",SearchOption.AllDirectories).Select(f=>Path.GetRelativePath(stage,f).Replace('\\','/')).ToArray();
            // Tout ce qui peut échouer (fichier verrouillé, disque plein…) AVANT de toucher à l'installation du joueur.
            RestoreUserFiles(clientDir, stage);
            if(Directory.Exists(clientDir) && ClientUninstaller.CanReinstall(clientDir)) ClientUninstaller.RestoreRetainedFiles(clientDir,stage);
            await File.WriteAllTextAsync(Path.Combine(stage,".cotk-managed-files.json"),JsonSerializer.Serialize(owned),ct);
            await File.WriteAllTextAsync(Path.Combine(stage, MarkerName), release.Version + Environment.NewLine, ct);
            Directory.CreateDirectory(Path.GetDirectoryName(trimmed)!);
            if (Directory.Exists(clientDir)) Directory.Move(clientDir, backup);
            Directory.Move(stage, clientDir);
            installed = true;
            progress.Report(new TransferProgress(TransferPhase.Done, $"Client {release.Version} installé."));
        }
        catch
        {
            if (Directory.Exists(backup))
            {
                if (Directory.Exists(clientDir)) Directory.Delete(clientDir, true);
                Directory.Move(backup, clientDir);
            }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
            if (installed) try { Directory.Delete(backup, true); } catch { }
            try { File.Delete(archive); } catch { }
        }
    }

    private static void RestoreUserFiles(string existingDir, string newDir)
    {
        if (!Directory.Exists(existingDir)) return;
        foreach (var source in Directory.EnumerateFiles(existingDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(existingDir, source);
            if (!ManifestRules.IsPlayerData(relative)) continue;
            var destination = Path.Combine(newDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    internal static void ExtractZipSafely(string archivePath, string destination, IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        var total = Math.Max(1L, archive.Entries.Sum(e => e.Length));
        long done = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var last = 0L;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!ManifestRules.IsSafeRelative(entry.FullName.TrimEnd('/', '\\')))
                throw new LauncherException(Errors.UnsafeUpdate("L'archive contient un nom de fichier invalide."));
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new LauncherException(Errors.UnsafeUpdate("L'archive contient un chemin de sortie invalide."));
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            done += entry.Length;
            if (watch.ElapsedMilliseconds - last > 150)
            {
                last = watch.ElapsedMilliseconds;
                progress?.Report(new TransferProgress(TransferPhase.Extracting, "Extraction des fichiers…", done, total));
            }
        }
    }

    // ------------------------------------------------------------------ utilitaires

    internal static string LocalPath(string clientDir, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(clientDir, relative.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(clientDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new LauncherException(Errors.UnsafeUpdate($"Chemin hors du dossier du jeu : {relative}"));
        // Un lien/junction dans l'installation peut contourner le contrôle du préfixe.
        var current = full;
        while (current.Length >= root.TrimEnd(Path.DirectorySeparatorChar).Length)
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new LauncherException(Errors.UnsafeUpdate("Un lien symbolique ou une jonction a été trouvé dans le dossier du jeu."));
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current) break;
            current = parent;
        }
        return full;
    }

    internal static void EnsureDiskSpace(string path, long needed)
    {
        StorageCheck.Ensure(path,needed);
    }
}
