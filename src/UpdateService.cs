using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace COTK.Launcher;

internal sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
    };

    private readonly HttpClient _http;
    private readonly HttpClient _downloadHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _apiUrl;

    /// <summary>Confirme avant de telecharger un gros client. Parametre : taille
    /// en octets. Retour : true pour lancer le telechargement. Si null, telecharge
    /// directement (comportement historique).</summary>
    internal Func<long, Task<bool>>? ConfirmClientDownload { get; set; }

    /// <summary>Demande a l'utilisateur ou installer le client (premiere
    /// installation uniquement). Retour : dossier choisi, ou null pour annuler.</summary>
    internal Func<Task<string?>>? PickClientFolder { get; set; }

    internal UpdateService()
    {
_apiUrl = LauncherConfig.ApiUrl.TrimEnd('/');
        if (!Uri.TryCreate(_apiUrl + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || (baseUri.Scheme == Uri.UriSchemeHttp && !baseUri.IsLoopback))
        {
            throw new InvalidOperationException("COTK_API_URL must be an absolute HTTP or HTTPS URL.");
        }

        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(30) };
    }

    internal async Task<UpdateManifest?> GetManifestAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("api/v1/launcher/updates", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new UpdateException($"Le manifeste de versions a rÃ©pondu HTTP {(int)response.StatusCode}.");

        try
        {
            var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Manifeste vide.");
            ValidateManifest(manifest);
            return manifest;
        }
        catch (JsonException ex)
        {
            throw new UpdateException("Le manifeste de versions est invalide.", ex);
        }
    }

    /// <summary>Verifie si une mise a jour du launcher est publiee.
    /// Retour : la release a installer, ou null si le launcher est a jour.</summary>
    internal async Task<LauncherRelease?> CheckLauncherUpdateAsync(CancellationToken cancellationToken)
    {
        var manifest = await GetManifestAsync(cancellationToken)
            ?? throw new UpdateException("Manifeste de versions indisponible.");
        ValidateManifest(manifest);
        return IsNewer(manifest.Launcher.Version, CurrentLauncherVersion()) ? manifest.Launcher : null;
    }

    /// <summary>Release client attendue selon le manifeste publie.</summary>
    internal async Task<ClientRelease> GetClientReleaseAsync(CancellationToken cancellationToken)
    {
        var manifest = await GetManifestAsync(cancellationToken)
            ?? throw new UpdateException("Manifeste de versions indisponible.");
        ValidateManifest(manifest);
        return manifest.Client;
    }

    /// <summary>Le client est considere pret uniquement si l'executable existe
    /// ET que le marqueur de version correspond au manifeste. Un client perime
    /// ne doit jamais pouvoir etre lance.</summary>
    internal static bool IsClientInstalled(ClientRelease release)
    {
        var clientDir = GameLauncher.ClientDir;
        var marker = Path.Combine(clientDir, ".cotk-client-version");
        try
        {
            return File.Exists(Path.Combine(clientDir, release.Executable))
                && File.Exists(marker)
                && string.Equals(File.ReadAllText(marker).Trim(), release.Version, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    internal static string CurrentLauncherVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    internal static bool IsNewer(string remote, string local)
    {
        if (!Version.TryParse(remote, out var remoteVersion) || !Version.TryParse(local, out var localVersion))
            throw new UpdateException($"Version invalide : remote={remote}, local={local}.");
        return remoteVersion > localVersion;
    }

    internal static void ValidateManifest(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new UpdateException($"Version de manifeste non supportÃ©e : {manifest.SchemaVersion}.");
        ValidateRelease(manifest.Launcher.Version, manifest.Launcher.DownloadUrl, manifest.Launcher.Sha256, manifest.Launcher.SizeBytes, false);
        ValidateRelease(manifest.Client.Version, manifest.Client.ArchiveUrl, manifest.Client.Sha256, manifest.Client.SizeBytes, true);
        if (string.IsNullOrWhiteSpace(manifest.Client.Executable)
            || Path.IsPathRooted(manifest.Client.Executable)
            || manifest.Client.Executable.Contains("..", StringComparison.Ordinal))
            throw new UpdateException("Le chemin de l'exÃ©cutable client est invalide.");
    }

    private static void ValidateRelease(string version, string url, string sha256, long sizeBytes, bool required)
    {
        if (!Version.TryParse(version, out _))
            throw new UpdateException($"Version de release invalide : {version}.");
        if (sizeBytes < 0 || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new UpdateException($"Empreinte ou taille invalide pour la release {version}.");
        if (required && string.IsNullOrWhiteSpace(url))
            throw new UpdateException($"URL absente pour la release obligatoire {version}.");
        if (url.Length > 0)
            ValidateDownloadUrl(url);
    }

    private static void ValidateDownloadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || (uri.Scheme == Uri.UriSchemeHttp
                && !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase)))
            throw new UpdateException("Une archive distante doit utiliser HTTPS; HTTP est rÃ©servÃ© Ã  localhost.");
    }

    internal async Task EnsureClientAsync(ClientRelease release, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var clientDir = GameLauncher.ClientDir;
        var executable = Path.Combine(clientDir, release.Executable);
        var marker = Path.Combine(clientDir, ".cotk-client-version");
        if (File.Exists(executable)
            && File.Exists(marker)
            && string.Equals(File.ReadAllText(marker).Trim(), release.Version, StringComparison.Ordinal))
            return;

        if (string.IsNullOrWhiteSpace(release.ArchiveUrl))
        {
            if (File.Exists(executable))
            {
                File.WriteAllText(marker, release.Version + Environment.NewLine);
                GameLauncher.Log($"Existing client accepted as version {release.Version}; no archive configured");
                return;
            }
            throw new UpdateException("Le client est absent et aucune archive n'est configurÃ©e.");
        }

        var isUpdate = File.Exists(executable);

        // Mise a jour d'un client existant : chemin differentiel (catalogue
        // fichier par fichier) quand le manifeste en fournit un. Moins de
        // donnees a telecharger, pas de double espace disque. En cas de
        // probleme, repli automatique sur l'archive complete.
        if (isUpdate && !string.IsNullOrWhiteSpace(release.FilesUrl))
        {
            try
            {
                await SyncClientDeltaAsync(release, progress, cancellationToken);
                await File.WriteAllTextAsync(marker, release.Version + Environment.NewLine, cancellationToken);
                GameLauncher.Log($"client delta update: done, version {release.Version}");
                progress?.Report($"Client {release.Version} a jour.");
                return;
            }
            catch (ClientDownloadDeclinedException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                GameLauncher.Log($"delta update failed, full archive fallback: {ex.GetType().Name}: {ex.Message}");
                progress?.Report("Mise a jour differentielle indisponible, telechargement complet...");
            }
        }
        else if (isUpdate && ConfirmClientDownload is not null)
        {
            // Sans catalogue, une mise a jour repasse par l'archive complete :
            // on demande confirmation avant de re-telecharger 15 Go.
            if (!await ConfirmClientDownload(release.SizeBytes))
                throw new ClientDownloadDeclinedException();
        }

        progress?.Report($"TÃ©lÃ©chargement du client {release.Version}...");

        if (!File.Exists(executable))
        {
            // Premiere installation : ou installer le client ? On ne demande que
            // si aucun emplacement valide n'est deja memorise (%APPDATA%).
            if (PickClientFolder is not null && LauncherSettings.ClientDirectory is null)
            {
                var chosen = await PickClientFolder();
                if (string.IsNullOrWhiteSpace(chosen))
                    throw new ClientDownloadDeclinedException();

                // L'utilisateur designe l'emplacement PARENT ; on installe dans
                // un sous-dossier "client" pour ne jamais deplacer/renommer un
                // dossier existant lui appartenant (ex. D:\Games).
                chosen = Path.Combine(chosen, "client");
                GameLauncher.SetClientDir(chosen);
                clientDir = GameLauncher.ClientDir;
                executable = Path.Combine(clientDir, release.Executable);
                marker = Path.Combine(clientDir, ".cotk-client-version");
            }

            if (ConfirmClientDownload is not null)
            {
                var approved = await ConfirmClientDownload(release.SizeBytes);
                if (!approved)
                    throw new ClientDownloadDeclinedException();
            }
        }

        var archive = await DownloadVerifiedAsync(release.ArchiveUrl, release.Sha256, release.SizeBytes, "client", progress, cancellationToken);
        await InstallClientArchiveAsync(archive, release, progress, cancellationToken);
    }

    /// <summary>Mise a jour differentielle : compare le catalogue publie aux
    /// fichiers locaux, ne telecharge que les differences (4 en parallele),
    /// supprime les fichiers disparus. Les donnees joueur (ClientConfig.ini,
    /// Logs, marqueur) ne sont jamais touchees.</summary>
    private async Task SyncClientDeltaAsync(
        ClientRelease release,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(release.FilesUrl))
            throw new UpdateException("Aucun catalogue differentiel publie.");
        ValidateDownloadUrl(release.FilesUrl);

        progress?.Report("Analyse des fichiers locaux...");
        var catalogJson = await _http.GetStringAsync(release.FilesUrl, cancellationToken);
        var catalog = JsonSerializer.Deserialize<ClientFileCatalog>(catalogJson, JsonOptions)
            ?? throw new UpdateException("Catalogue differentiel illisible.");
        if (catalog.Files is null || catalog.Files.Count == 0)
            throw new UpdateException("Catalogue differentiel vide.");

        var clientDir = GameLauncher.ClientDir;
        var baseUrl = !string.IsNullOrWhiteSpace(catalog.BaseUrl)
            ? catalog.BaseUrl.TrimEnd('/') + "/"
            : release.FilesUrl.Replace("client-files.json", "client/");

        var toDownload = new List<ClientFileEntry>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Files)
        {
            if (entry.Path.Contains("..") || Path.IsPathRooted(entry.Path))
                throw new UpdateException($"Chemin invalide dans le catalogue : {entry.Path}.");
            known.Add(entry.Path);

            var local = Path.Combine(clientDir, entry.Path.Replace('/', '\\'));
            if (File.Exists(local)
                && new FileInfo(local).Length == entry.Size
                && string.Equals(await Sha256Async(local), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                continue;
            toDownload.Add(entry);
        }
        GameLauncher.Log($"delta: {toDownload.Count}/{catalog.Files.Count} fichiers a mettre a jour");

        // Fichiers disparus du catalogue : suppression (hors donnees joueur).
        foreach (var file in Directory.EnumerateFiles(clientDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(clientDir, file).Replace('\\', '/');
            if (known.Contains(rel) || IsPlayerData(rel)) continue;
            File.Delete(file);
            GameLauncher.Log($"delta: suppression de {rel} (absent du catalogue)");
        }
        var staleDirs = Directory.EnumerateDirectories(clientDir, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length)
            .ToList();
        foreach (var dir in staleDirs)
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                try { Directory.Delete(dir); } catch { }
            }
        }

        if (toDownload.Count == 0)
        {
            progress?.Report("Client deja a jour fichier par fichier.");
            return;
        }

        var totalBytes = toDownload.Sum(f => f.Size);
        var doneFiles = 0;
        var doneBytes = 0L;
        var gate = new SemaphoreSlim(4);
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var progressLock = new object();

        var tasks = toDownload.Select(entry => Task.Run(async () =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await DownloadClientFileAsync(baseUrl, entry, clientDir, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
                return;
            }
            finally
            {
                gate.Release();
            }
            lock (progressLock)
            {
                doneFiles++;
                doneBytes += entry.Size;
                progress?.Report(
                    $"Mise a jour du client : {doneFiles}/{toDownload.Count} fichiers ({doneBytes * 100 / Math.Max(1, totalBytes)} %)");
            }
        }, cancellationToken));
        await Task.WhenAll(tasks);

        if (errors.Count > 0)
            throw new AggregateException("Des fichiers n'ont pas pu etre telecharges.", errors);
    }

    private async Task DownloadClientFileAsync(
        string baseUrl,
        ClientFileEntry entry,
        string clientDir,
        CancellationToken cancellationToken)
    {
        var local = Path.Combine(clientDir, entry.Path.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        var url = baseUrl + string.Join('/', entry.Path.Split('/').Select(Uri.EscapeDataString));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await _downloadHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(local, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
                await input.CopyToAsync(output, cancellationToken);
                break;
            }
            catch (Exception ex)
                when (attempt < 3 && ex is IOException or HttpRequestException && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        if (!string.Equals(await Sha256Async(local), entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(local);
            throw new UpdateException($"SHA-256 invalide pour {entry.Path}.");
        }
    }

    private static bool IsPlayerData(string relativePath)
    {
        return relativePath.Equals("ClientConfig.ini", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(".cotk-client-version", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("Logs", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("Logs/", StringComparison.OrdinalIgnoreCase);
    }

    internal async Task ScheduleLauncherUpdateAsync(
        LauncherRelease release,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var archive = await DownloadVerifiedAsync(release.DownloadUrl, release.Sha256, release.SizeBytes, "launcher", progress, cancellationToken);
        var planPath = Path.Combine(Path.GetTempPath(), $"cotk-launcher-update-{Guid.NewGuid():N}.json");
        var updaterDirectory = Path.Combine(Path.GetTempPath(), $"COTK.Launcher.Updater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(updaterDirectory);

        // Copie TOUT le runtime self-contained (hostfxr, coreclr, System.*...):
        // avec seulement COTK.Launcher.*, l'updater ne demarre jamais et la
        // mise a jour echoue silencieusement. On saute les logs/donnees locales.
        foreach (var runtimeFile in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(AppContext.BaseDirectory, runtimeFile);
            if (relative.StartsWith("launcher" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("client" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.Combine(updaterDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(runtimeFile, target, overwrite: true);
        }
        var updaterExecutable = Path.Combine(updaterDirectory, Path.GetFileName(Environment.ProcessPath!));
        var plan = new LauncherUpdatePlan(
            archive,
            AppContext.BaseDirectory,
            Environment.ProcessPath!,
            Environment.ProcessId,
            updaterExecutable);
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        var psi = new ProcessStartInfo
        {
            FileName = updaterExecutable,
            WorkingDirectory = Path.GetDirectoryName(updaterExecutable)!,
            UseShellExecute = true,
        };
        psi.ArgumentList.Add("--apply-launcher-update");
        psi.ArgumentList.Add(planPath);
        _ = Process.Start(psi) ?? throw new UpdateException("Impossible de dÃ©marrer l'installateur du launcher.");
    }

    internal static async Task<int> ApplyLauncherUpdateAsync(string planPath)
    {
        LauncherUpdatePlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<LauncherUpdatePlan>(await File.ReadAllTextAsync(planPath), JsonOptions)
                ?? throw new JsonException("Plan vide.");
            ValidateArchivePath(plan.ArchivePath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or UpdateException)
        {
            GameLauncher.Log($"apply-update: plan illisible ({ex.GetType().Name}: {ex.Message})");
            return 1;
        }

        try
        {
            using var parent = Process.GetProcessById(plan.ParentProcessId);
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (ArgumentException) { }
        catch (TimeoutException)
        {
            GameLauncher.Log("apply-update: le launcher parent n'a pas ferme en 2 minutes, abandon.");
            return 1;
        }

        var stage = plan.TargetDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".update-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            ExtractZipSafely(plan.ArchivePath, stage);
            var sourceExe = Path.Combine(stage, Path.GetFileName(plan.LauncherExecutable));
            if (!File.Exists(sourceExe)) throw new FileNotFoundException("Executable absent de l'archive.", sourceExe);

            // Si un ancien updater tourne encore depuis TargetDirectory, ses
            // fichiers sont verrouilles : on retente quelques fois.
            for (var copyAttempt = 1; ; copyAttempt++)
            {
                try
                {
                    foreach (var source in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(stage, source);
                        var destination = Path.Combine(plan.TargetDirectory, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(source, destination, overwrite: true);
                    }
                    break;
                }
                catch (IOException) when (copyAttempt < 5)
                {
                    GameLauncher.Log($"apply-update: fichiers verrouilles (antivirus ?), nouvel essai {copyAttempt +1}/5 dans 3s...");
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
            }

            GameLauncher.Log("apply-update: fichiers remplaces, relancement du launcher.");
            Process.Start(new ProcessStartInfo
            {
                FileName = plan.LauncherExecutable,
                WorkingDirectory = plan.TargetDirectory,
                UseShellExecute = true,
                Arguments = $"--cleanup-updater \"{plan.UpdaterExecutable}\"",
            });
            return 0;
        }
        catch (Exception ex)
        {
            GameLauncher.Log($"apply-update: ECHEC ({ex.GetType().Name}: {ex.Message})");
            return 1;
        }
        finally
        {
            try { Directory.Delete(stage, true); } catch { }
            try { File.Delete(planPath); } catch { }
            try { File.Delete(plan.ArchivePath); } catch { }
        }
    }

    internal async Task<string> DownloadVerifiedAsync(
        string url,
        string expectedSha256,
        long expectedSize,
        string name,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ValidateDownloadUrl(url);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"cotk-{name}-{expectedSha256[..12].ToLowerInvariant()}.part");

        // Reprise : jusqu'a 6 tentatives, chaque tentative reprend ou le flux
        // s'est arrete (HTTP Range). Le .part est deterministe, donc un
        // relancement du launcher reprenne aussi le telechargement.
        const int maxAttempts = 6;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await DownloadOnceAsync(url, path, expectedSha256, expectedSize, name, progress, cancellationToken);
                break;
            }
            catch (Exception ex)
                when (attempt < maxAttempts
                    && ex is HttpRequestException or IOException or TaskCanceledException
                    && !cancellationToken.IsCancellationRequested
                    && ex is not ClientDownloadDeclinedException)
            {
                var wait = TimeSpan.FromSeconds(2 * attempt);
                GameLauncher.Log($"download {name}: tentative {attempt} interrompue ({ex.GetType().Name}: {ex.Message}). Reprise dans {wait.TotalSeconds}s...");
                progress?.Report($"Connexion interrompue. Reprise du téléchargement dans {wait.TotalSeconds} s...");
                await Task.Delay(wait, cancellationToken);
            }
        }

        progress?.Report($"Vérification de l'archive {name}...");
        if (!string.Equals(await Sha256Async(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new UpdateException($"Vérification SHA-256 échouée pour l'archive {name}.");
        }
        return path;
    }

    private async Task DownloadOnceAsync(
        string url,
        string path,
        string expectedSha256,
        long expectedSize,
        string name,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        long offset = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (offset > expectedSize)
        {
            File.Delete(path);
            offset = 0;
        }
        if (expectedSize > 0 && offset == expectedSize)
        {
            // Le .part est deja complet (tentative precedente) : inutile de
            // recontacter le serveur, la verification SHA-256 tranchera.
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);

        // Timeout infini : la limite de 30 min du client API tuait les gros
        // telechargements. Une garde anti-blocage est appliquee par lecture.
        using var response =
            await _downloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && offset >= expectedSize)
        {
            // 416 alors qu'on a deja tous les octets : fichier complet.
            return;
        }
        if (offset > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            // Le serveur a ignore le Range : on repart de zero.
            offset = 0;
        }
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length
            && expectedSize > 0 && offset + length != expectedSize)
            throw new UpdateException($"Taille inattendue pour l'archive {name}.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            path,
            offset > 0 ? FileMode.Append : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 64,
            useAsync: true);

        var buffer = new byte[1024 * 64];
        long total = offset;
        var lastReport = total;
        int read;

        // Garde anti-blocage : si aucun octet n'arrive pendant 5 minutes,
        // la tentative echoue et la boucle de reprise prend le relais.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (true)
        {
            stall.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                read = await input.ReadAsync(buffer, stall.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HttpRequestException("Connexion interrompue (aucune donnee depuis 5 minutes).");
            }

            if (read == 0) break;
            total += read;
            if (expectedSize > 0 && total > expectedSize)
                throw new UpdateException($"Archive {name} trop volumineuse.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

            if (expectedSize > 0 && total - lastReport >= expectedSize / 100)
            {
                lastReport = total;
                progress?.Report($"Téléchargement {name}: {total * 100 / expectedSize} %");
            }
        }

        await output.FlushAsync(cancellationToken);
        if (expectedSize > 0 && total != expectedSize)
            throw new HttpRequestException($"Flux termine prematurément ({total}/{expectedSize} octets).");
    }

    private static async Task InstallClientArchiveAsync(string archive, ClientRelease release, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var clientDir = GameLauncher.ClientDir;
        var stage = clientDir.TrimEnd(Path.DirectorySeparatorChar) + ".update-" + Guid.NewGuid().ToString("N");
        var backup = clientDir.TrimEnd(Path.DirectorySeparatorChar) + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            ExtractZipSafely(archive, stage);
            var stagedExe = Path.Combine(stage, release.Executable);
            if (!File.Exists(stagedExe))
                throw new UpdateException($"L'archive ne contient pas {release.Executable} à sa racine.");

            // Securite : si le dossier client existe deja mais ne ressemble pas a
            // une installation (pas de H1Z1.exe, non vide), on ne le touche pas.
            if (Directory.Exists(clientDir)
                && Directory.EnumerateFileSystemEntries(clientDir).Any()
                && !File.Exists(Path.Combine(clientDir, release.Executable)))
                throw new UpdateException(
                    $"Le dossier client choisi ({clientDir}) existe et n'est pas vide. Choisissez un emplacement dedie ou videz-le.");

            Directory.CreateDirectory(Path.GetDirectoryName(clientDir)!);
            if (Directory.Exists(clientDir))
                Directory.Move(clientDir, backup);
            Directory.Move(stage, clientDir);
            RestoreUserFiles(backup, clientDir);
            await File.WriteAllTextAsync(Path.Combine(clientDir, ".cotk-client-version"), release.Version + Environment.NewLine, cancellationToken);
            progress?.Report($"Client {release.Version} installÃ©.");
        }
        catch
        {
            if (!Directory.Exists(clientDir) && Directory.Exists(backup)) Directory.Move(backup, clientDir);
            throw;
        }
        finally
        {
            try { Directory.Delete(stage, true); } catch { }
            try { Directory.Delete(backup, true); } catch { }
            try { File.Delete(archive); } catch { }
        }
    }

    private static void RestoreUserFiles(string backup, string clientDir)
    {
        if (!Directory.Exists(backup)) return;
        foreach (var relative in new[] { "ClientConfig.ini", "Logs" })
        {
            var source = Path.Combine(backup, relative);
            var destination = Path.Combine(clientDir, relative);
            if (File.Exists(source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            else if (Directory.Exists(source))
            {
                CopyDirectory(source, destination);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static void ValidateArchivePath(string path)
    {
        if (!Path.IsPathRooted(path) || !File.Exists(path) || !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("Archive locale invalide.");
    }

    private static void ExtractZipSafely(string archivePath, string destination)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("L'archive contient un chemin de sortie invalide.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}

internal sealed record UpdateResult(bool ManifestAvailable, bool RestartRequired, string Message, string? ClientVersion = null);
