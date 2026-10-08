using System.Diagnostics;

namespace COTK.Launcher;

/// <summary>Console F8 staff (ToggleConsole) a la demande : le launcher
/// telecharge l'archive depuis l'API (endpoint staff-only), verifie son
/// SHA-256, l'extrait a cote de lui, puis le circuit existant
/// (GameLauncher.StartToggleConsole) l'ouvre en jeu. Le SHA-256 du zip EST
/// la version : tout rebuild change l'empreinte et declenche "mettre a jour".
/// Sans role staff, le serveur refuse (403) et rien n'est propose.</summary>
internal static class StaffConsole
{
    internal const string ExeName = "ToggleConsole.exe";
    private const string MarkerName = "console.sha256";

    internal static string Directory => Path.Combine(AppContext.BaseDirectory, "ToggleConsole");

    internal static string ExePath => Path.Combine(Directory, ExeName);

    private static string MarkerPath => Path.Combine(Directory, MarkerName);

    internal static string? InstalledSha()
    {
        try
        {
            if (!File.Exists(ExePath) || !File.Exists(MarkerPath)) return null;
            var sha = File.ReadAllText(MarkerPath).Trim().ToLowerInvariant();
            return sha.Length == 64 && sha.All(Uri.IsHexDigit) ? sha : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Logique pure (testee) : faut-il proposer installer / mettre a jour ?</summary>
    internal static bool ShouldInstall(string? installedSha, StaffConsoleInfo info)
        => !string.Equals(installedSha, info.Sha256, StringComparison.OrdinalIgnoreCase);

    internal static async Task InstallAsync(
        AuthApiClient api, string accessToken, StaffConsoleInfo info, string operationId, CancellationToken ct)
    {
        var stage = Path.Combine(Path.GetTempPath(), "cotk-staffconsole-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(stage);
        try
        {
            var archive = Path.Combine(stage, "toggleconsole.zip.part");
            GameLauncher.Log($"op={operationId} staff console download {info.SizeBytes} bytes");
            await api.DownloadStaffConsoleAsync(accessToken, archive, ct);

            var sha = await UpdateService.Sha256Async(archive);
            if (!string.Equals(sha, info.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archive corrompue (SHA-256 inattendu). Réessayez.");

            var extracted = Path.Combine(stage, "extracted");
            System.IO.Directory.CreateDirectory(extracted);
            UpdateService.ExtractZipSafely(archive, extracted);
            var exe = Path.Combine(extracted, ExeName);
            if (!File.Exists(exe))
                throw new InvalidOperationException("Archive invalide (ToggleConsole.exe absent).");

            // Le watcher tourne tant que le jeu est ouvert : l'installation se
            // fait jeu ferme, mais on tue les residus plutot que d'echouer sur
            // un fichier verrouille.
            foreach (var stale in Process.GetProcessesByName("ToggleConsole"))
            {
                try { stale.Kill(); } catch { } finally { stale.Dispose(); }
            }

            var dir = Directory;
            if (System.IO.Directory.Exists(dir))
            {
                try { System.IO.Directory.Delete(dir, recursive: true); }
                catch (IOException ex)
                {
                    throw new InvalidOperationException(
                        "Dossier ToggleConsole verrouillé (jeu ouvert ?). Fermez le jeu et réessayez.", ex);
                }
            }
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
            System.IO.Directory.Move(extracted, dir);
            await File.WriteAllTextAsync(MarkerPath, info.Sha256, ct);
            GameLauncher.Log($"op={operationId} staff console installed sha={info.Sha256[..12]}...");
        }
        finally
        {
            try { System.IO.Directory.Delete(stage, recursive: true); } catch { }
        }
    }
}
