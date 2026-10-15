using System.Diagnostics;
using System.Text.Json;

namespace COTK.Next.Core;

/// <summary>Plan transmis au processus qui remplace les fichiers (même format que l'ancien launcher WinForms,
/// dont les installations existantes appliquent la même séquence pour migrer vers ce launcher).</summary>
internal sealed record LauncherUpdatePlan(string ArchivePath, string TargetDirectory, string LauncherExecutable, int ParentProcessId, string UpdaterExecutable);

/// <summary>Mise à jour du launcher lui-même. Le paquet est un zip dont la racine contient COTK.Launcher.exe
/// (nom attendu par les anciens launchers, qui relancent cet exécutable après avoir copié les fichiers).
/// Séquence : téléchargement vérifié (SHA-256, taille) → copie du runtime dans %TEMP% → ce processus-copie attend la
/// fermeture du launcher, sauvegarde les fichiers remplacés, copie, relance, et restaure tout en cas d'échec.</summary>
internal static class LauncherUpdater
{
    /// <summary>Nom d'exécutable que les anciens launchers (0.x) exigent à la racine du zip et relancent ensuite.</summary>
    public const string CompatExecutable = "COTK.Launcher.exe";

    private const string UpdaterPrefix = "COTK.Launcher.Updater-";

    // Dossiers du joueur : un paquet qui les contient est refusé, on n'écrase jamais ses données.
    private static readonly string[] ProtectedTopLevel = { "client", "logs", "ToggleConsole", "unins", "settings" };

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(8),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    }) { Timeout = Timeout.InfiniteTimeSpan };

    // ------------------------------------------------------------------ détection

    public static bool IsNewer(string remote, string local) =>
        Version.TryParse(remote, out var r) && Version.TryParse(local, out var l) && r > l;

    /// <summary>La release à installer, ou null si le launcher est à jour ou si aucun paquet n'est publié.</summary>
    public static LauncherRelease? Check(UpdateManifest manifest, string? currentVersion = null)
    {
        var release = manifest.Launcher;
        if (string.IsNullOrWhiteSpace(release.DownloadUrl) || release.SizeBytes <= 0) return null;
        return IsNewer(release.Version, currentVersion ?? Config.Version) ? release : null;
    }

    // ------------------------------------------------------------------ téléchargement

    /// <summary>Télécharge le paquet vers un fichier temporaire. Jamais plus que la taille publiée ; le fichier n'est
    /// rendu que s'il correspond exactement à l'empreinte du manifeste (sinon supprimé).</summary>
    public static async Task<string> DownloadAsync(LauncherRelease release, IProgress<TransferProgress>? progress, CancellationToken ct, string? directory = null)
    {
        ManifestRules.ValidateUrl(release.DownloadUrl);
        var path = Path.Combine(directory ?? Path.GetTempPath(), $"cotk-launcher-{release.Sha256[..12].ToLowerInvariant()}.part");
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                using var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } announced && announced != release.SizeBytes)
                    throw new LauncherException(Errors.UnsafeUpdate("La taille du paquet ne correspond pas à celle publiée."));

                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        total += read;
                        if (total > release.SizeBytes)
                            throw new LauncherException(Errors.UnsafeUpdate("Le paquet reçu est plus gros que prévu."));
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        progress?.Report(new TransferProgress(TransferPhase.Downloading, "Téléchargement du launcher…", total, release.SizeBytes));
                    }
                    if (total != release.SizeBytes)
                        throw new HttpRequestException($"Flux interrompu ({total}/{release.SizeBytes} octets).");
                }

                progress?.Report(new TransferProgress(TransferPhase.Verifying, "Vérification de l'intégrité…", release.SizeBytes, release.SizeBytes));
                var sha = await Hashing.Sha256Async(path, ct);
                if (!string.Equals(sha, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new LauncherException(Errors.HashMismatch("launcher"));
                return path;
            }
            catch (LauncherException) { TryDelete(path); throw; }
            catch (OperationCanceledException) { TryDelete(path); throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                last = ex;
                Log.Warn($"téléchargement du launcher interrompu (essai {attempt}/3)", ex);
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }
        TryDelete(path);
        throw new LauncherException(Errors.Offline(last?.Message), last);
    }

    // ------------------------------------------------------------------ lancement du remplaçant

    /// <summary>Copie le launcher dans %TEMP% et lance la copie en mode « appliquer ». Le processus courant doit
    /// ensuite se fermer : c'est la copie qui remplace les fichiers et relance le nouveau launcher.</summary>
    public static void Schedule(string archive)
    {
        var updaterDirectory = Path.Combine(Path.GetTempPath(), UpdaterPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updaterDirectory);
        var source = AppContext.BaseDirectory;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsProtected(relative) || relative.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(updaterDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        var current = Environment.ProcessPath ?? throw new LauncherException(Errors.UnsafeUpdate("Chemin du launcher introuvable."));
        var updater = Path.Combine(updaterDirectory, Path.GetFileName(current));
        var plan = new LauncherUpdatePlan(archive, source, current, Environment.ProcessId, updater);
        var planPath = Path.Combine(Path.GetTempPath(), $"cotk-launcher-update-{Guid.NewGuid():N}.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, ApiClient.Json));

        var info = new ProcessStartInfo { FileName = updater, WorkingDirectory = updaterDirectory, UseShellExecute = true };
        info.ArgumentList.Add("--apply-launcher-update");
        info.ArgumentList.Add(planPath);
        _ = Process.Start(info) ?? throw new LauncherException(Errors.UnsafeUpdate("Impossible de démarrer l'installateur du launcher."));
        Log.Info("mise à jour du launcher planifiée");
    }

    // ------------------------------------------------------------------ application (processus-copie)

    /// <summary>Remplace les fichiers du launcher. Retourne 0 si le nouveau launcher a été relancé, 1 sinon (et alors
    /// l'installation d'origine est restaurée et relancée). <paramref name="launch"/> reçoit (exécutable, arguments).</summary>
    public static int Apply(string planPath, Func<string, string, bool>? launch = null, TimeSpan? retryDelay = null, TimeSpan? parentWait = null)
    {
        launch ??= StartProcess;
        var delay = retryDelay ?? TimeSpan.FromSeconds(3);
        LauncherUpdatePlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<LauncherUpdatePlan>(File.ReadAllText(planPath), ApiClient.Json) ?? throw new JsonException("Plan vide.");
            var extension = Path.GetExtension(plan.ArchivePath);
            if (!Path.IsPathRooted(plan.ArchivePath) || !File.Exists(plan.ArchivePath)
                || !(extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) || extension.Equals(".part", StringComparison.OrdinalIgnoreCase)))
                throw new LauncherException(Errors.UnsafeUpdate("Archive locale invalide."));
            if (!Directory.Exists(plan.TargetDirectory) || !Path.IsPathRooted(plan.LauncherExecutable))
                throw new LauncherException(Errors.UnsafeUpdate("Dossier du launcher invalide."));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or LauncherException)
        {
            Log.Warn("mise à jour du launcher : plan illisible", ex);
            return 1;
        }

        try
        {
            using var parent = Process.GetProcessById(plan.ParentProcessId);
            if (!parent.WaitForExit(parentWait ?? TimeSpan.FromMinutes(2)))
            {
                Log.Warn("mise à jour du launcher : l'ancien launcher n'a pas fermé, abandon");
                return 1;
            }
        }
        catch (ArgumentException) { /* déjà fermé */ }

        var target = plan.TargetDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var stage = target + ".update-" + Guid.NewGuid().ToString("N");
        var backup = target + ".rollback-" + Guid.NewGuid().ToString("N");
        var created = new List<string>();
        var replaced = new List<string>();
        var newExecutable = plan.LauncherExecutable;
        try
        {
            Directory.CreateDirectory(stage);
            ClientInstaller.ExtractZipSafely(plan.ArchivePath, stage);

            var executableName = Path.GetFileName(plan.LauncherExecutable);
            if (!File.Exists(Path.Combine(stage, executableName)))
            {
                // Paquet publié sous le nom commun COTK.Launcher.exe alors que le launcher courant porte un autre nom.
                if (!File.Exists(Path.Combine(stage, CompatExecutable)))
                    throw new LauncherException(Errors.UnsafeUpdate($"Le paquet ne contient pas {CompatExecutable}."));
                executableName = CompatExecutable;
                newExecutable = Path.Combine(target, CompatExecutable);
            }

            foreach (var file in Directory.EnumerateFileSystemEntries(stage, "*", SearchOption.TopDirectoryOnly))
                if (ProtectedTopLevel.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
                    throw new LauncherException(Errors.UnsafeUpdate("Le paquet contient un dossier réservé aux données du joueur."));

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    foreach (var source in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(stage, source);
                        var destination = Path.Combine(target, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        if (File.Exists(destination))
                        {
                            if (!replaced.Contains(relative))
                            {
                                var saved = Path.Combine(backup, relative);
                                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                                File.Copy(destination, saved, overwrite: true);
                                replaced.Add(relative);
                            }
                        }
                        else if (!created.Contains(relative)) created.Add(relative);
                        File.Copy(source, destination, overwrite: true);
                    }
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    Log.Warn($"mise à jour du launcher : fichiers verrouillés, essai {attempt + 1}/5");
                    Thread.Sleep(delay);
                }
            }

            Log.Info("mise à jour du launcher : fichiers remplacés, relancement");
            var started = launch(newExecutable, $"--cleanup-updater \"{plan.UpdaterExecutable}\"");
            if (!started) throw new LauncherException(Errors.UnsafeUpdate("Le nouveau launcher n'a pas pu démarrer."));
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("mise à jour du launcher : ÉCHEC, restauration", ex);
            Rollback(target, backup, replaced, created);
            try { launch(plan.LauncherExecutable, $"--cleanup-updater \"{plan.UpdaterExecutable}\""); } catch { }
            return 1;
        }
        finally
        {
            TryDeleteDirectory(stage);
            TryDeleteDirectory(backup);
            TryDelete(planPath);
            TryDelete(plan.ArchivePath);
        }
    }

    private static void Rollback(string target, string backup, List<string> replaced, List<string> created)
    {
        foreach (var relative in replaced)
        {
            try { File.Copy(Path.Combine(backup, relative), Path.Combine(target, relative), overwrite: true); }
            catch (Exception ex) { Log.Error($"restauration impossible : {relative}", ex); }
        }
        foreach (var relative in created)
            TryDelete(Path.Combine(target, relative));
    }

    private static bool StartProcess(string executable, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true,
        });
        return process is not null;
    }

    // ------------------------------------------------------------------ nettoyage

    /// <summary>Supprime le dossier temporaire de la copie-installateur — uniquement s'il s'agit bien de l'un des nôtres
    /// (jamais un chemin arbitraire reçu en argument).</summary>
    public static void Cleanup(string updaterExecutable, string? tempRoot = null, int attempts = 20)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(updaterExecutable));
            var root = Path.GetFullPath(tempRoot ?? Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (directory is null || !string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase)) return;
            var name = Path.GetFileName(directory);
            if (!name.StartsWith(UpdaterPrefix, StringComparison.Ordinal) || name.Length != UpdaterPrefix.Length + 32) return;
            for (var i = 0; i < attempts && Directory.Exists(directory); i++)
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (Exception) { Thread.Sleep(500); } // l'installateur peut encore se terminer
            }
        }
        catch (Exception ex) { Log.Warn("nettoyage de l'installateur impossible", ex); }
    }

    private static bool IsProtected(string relative) =>
        ProtectedTopLevel.Contains(relative.Split(Path.DirectorySeparatorChar)[0], StringComparer.OrdinalIgnoreCase);

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
