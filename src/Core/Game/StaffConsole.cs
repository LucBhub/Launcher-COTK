using System.Diagnostics;

namespace COTK.Next.Core;

/// <summary>Console F8 du staff (ToggleConsole) : jamais distribuée aux joueurs, installée à la demande
/// depuis l'API (endpoint staff-only, SHA-256 vérifié) et ouverte en jeu en mode « native UI » uniquement
/// (JAMAIS --patch : les anciens patchs mémoire déclenchent le contrôle d'intégrité du client).</summary>
internal static class StaffConsole
{
    internal const string ExeName = "ToggleConsole.exe";
    private const string MarkerName = "console.sha256";

    /// <summary>Uniquement pour les tests : évite d'écrire dans le dossier du launcher.</summary>
    internal static string? TestDirectory { get; set; }

    public static string Directory => TestDirectory ?? Path.Combine(AppPaths.InstallDir, "ToggleConsole");
    public static string ExePath => Path.Combine(Directory, ExeName);
    private static string MarkerPath => Path.Combine(Directory, MarkerName);

    public static string? InstalledSha()
    {
        try
        {
            if (!File.Exists(ExePath) || !File.Exists(MarkerPath)) return null;
            var sha = File.ReadAllText(MarkerPath).Trim().ToLowerInvariant();
            return sha.Length == 64 && sha.All(Uri.IsHexDigit) ? sha : null;
        }
        catch { return null; }
    }

    public static bool ShouldInstall(string? installedSha, StaffConsoleInfo info) =>
        !string.Equals(installedSha, info.Sha256, StringComparison.OrdinalIgnoreCase);

    public static bool IsWatcherRunning() => Process.GetProcessesByName("ToggleConsole").Length > 0;

    public static void StartWatcher(Account account, Process game, string operationId)
    {
        if (!account.IsStaff) return;
        try
        {
            game.Refresh();
            if (game.HasExited || IsWatcherRunning()) return;
            if (!File.Exists(ExePath))
            {
                Log.Info($"op={operationId} ToggleConsole absent — console indisponible, le jeu n'est pas affecté");
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = "--native-ui --watch --key 77",
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Directory,
            });
            Log.Info($"op={operationId} ToggleConsole démarré");
        }
        catch (Exception ex)
        {
            Log.Warn($"op={operationId} ToggleConsole : échec", ex);
        }
    }

    public static async Task InstallAsync(ApiClient api, string accessToken, StaffConsoleInfo info, CancellationToken ct)
    {
        var stage = Path.Combine(Path.GetTempPath(), "cotk-staffconsole-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(stage);
        try
        {
            var archive = Path.Combine(stage, "toggleconsole.zip.part");
            await api.DownloadStaffConsoleAsync(accessToken, archive, info.SizeBytes, ct);

            var sha = await Hashing.Sha256Async(archive, ct);
            if (!string.Equals(sha, info.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new LauncherException(Errors.HashMismatch("console F8"));

            var extracted = Path.Combine(stage, "extracted");
            ClientInstaller.ExtractZipSafely(archive, extracted);
            if (!File.Exists(Path.Combine(extracted, ExeName)))
                throw new LauncherException(Errors.UnsafeUpdate("L'archive de la console ne contient pas ToggleConsole.exe."));

            foreach (var stale in Process.GetProcessesByName("ToggleConsole"))
            {
                try { stale.Kill(); } catch { } finally { stale.Dispose(); }
            }

            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
            CopyDirectory(extracted, Directory);
            await File.WriteAllTextAsync(MarkerPath, info.Sha256, ct);
            Log.Info($"console F8 installée ({info.Sha256[..12]}…)");
        }
        finally
        {
            try { System.IO.Directory.Delete(stage, recursive: true); } catch { }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        System.IO.Directory.CreateDirectory(destination);
        foreach (var file in System.IO.Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in System.IO.Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
