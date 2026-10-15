using System.Diagnostics;
using System.Text;

namespace COTK.Next.Core;

internal enum ClientStage { Starting, TitleScreen, LoadingWorld, InGame }

/// <summary>Fichiers et processus du client H1Z1 : ticket, configuration, journaux, détection de l'étape.
/// Logique reprise à l'identique de l'ancien launcher (validée en prod) — seuls la lecture des journaux
/// (fin de fichier uniquement) et la sécurité du ticket ont été durcies.</summary>
internal static class GameFiles
{
    internal const int DefaultClientLogLevel = 4;
    private const int LogTailBytes = 512 * 1024;

    public static string ClientDir => ClientInstaller.ClientDir;

    public static string ExePath => Path.Combine(ClientDir, "H1Z1.exe");

    public static string ClientConfigPath => Path.Combine(ClientDir, "ClientConfig.ini");

    public static string LogsDir => Path.Combine(ClientDir, "Logs");

    public static string LiveLogPath => Path.Combine(LogsDir, "H1Z1 PlayClient (Live).log");

    public static bool IsGameRunning()
    {
        var expected = Path.GetFullPath(ExePath);
        foreach (var process in Process.GetProcessesByName("H1Z1"))
        {
            try
            {
                if (!process.HasExited
                    && string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            finally { process.Dispose(); }
        }
        return false;
    }

    // ------------------------------------------------------------------ ticket / configuration

    public static void WriteGameTicket(GameTicket ticket)
    {
        if (ticket.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(10))
            throw new LauncherException(Errors.TicketRejected());
        if (!File.Exists(ExePath))
            throw new LauncherException(Errors.ExeMissing(ExePath));

        if (!File.Exists(ClientConfigPath))
        {
            var template = Path.Combine(AppPaths.InstallDir, "ClientConfig.example.ini");
            if (!File.Exists(template))
                throw new LauncherException(Errors.InvalidConfig("Le modèle ClientConfig.example.ini est absent de l'installation."));
            File.Copy(template, ClientConfigPath, overwrite: true);
        }

        var lines = BuildClientConfig(File.ReadAllLines(ClientConfigPath), ticket.Value, Config.GameServer);
        var temporary = $"{ClientConfigPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
            File.Move(temporary, ClientConfigPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
        Log.Info($"ticket de jeu écrit (expire {ticket.ExpiresAt:O})");
    }

    internal static List<string> BuildClientConfig(IEnumerable<string> source, string ticket, string gameServer)
    {
        var lines = source
            .Where(line => !line.TrimStart().StartsWith("SessionId=", StringComparison.OrdinalIgnoreCase)
                && !line.TrimStart().StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        EnforceClientLogLevel(lines);
        lines.Insert(0, "Server=" + gameServer);
        lines.Insert(0, $"SessionId={ticket}");
        return lines;
    }

    // Niveau 4 et pas 9 : à 9 le client écrit une ligne par pipeline de rendu (~3800/session) de façon
    // synchrone sur le thread d'image (saccades de 40-110 ms en ville). Les marqueurs d'étape lus par
    // DetectClientStage sont au niveau 4. La clé doit exister : absente, le client retombe sur 5.
    internal static int ClientLogLevel =>
        int.TryParse(Environment.GetEnvironmentVariable("COTK_CLIENT_LOG_LEVEL"), out var level) && level is >= 0 and <= 9
            ? level
            : DefaultClientLogLevel;

    internal static void EnforceClientLogLevel(List<string> lines)
    {
        lines.RemoveAll(line => line.TrimStart().StartsWith("LocalLogLevel=", StringComparison.OrdinalIgnoreCase));
        var entry = $"LocalLogLevel={ClientLogLevel}";
        var header = lines.FindIndex(line => line.Trim().Equals("[Logging]", StringComparison.OrdinalIgnoreCase));
        if (header >= 0)
        {
            lines.Insert(header + 1, entry);
            return;
        }

        if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add(string.Empty);
        lines.Add("[Logging]");
        lines.Add(entry);
    }

    // ------------------------------------------------------------------ étape du client

    internal static ClientStage DetectClientStage(string log)
    {
        var title = Math.Max(
            log.LastIndexOf("newState=GAMESTATE_TITLESCREEN", StringComparison.Ordinal),
            log.LastIndexOf("newState=cClientRunStateCharacterCreateOrDelete", StringComparison.Ordinal));
        var loading = Math.Max(
            Math.Max(
                log.LastIndexOf("newState=GAMESTATE_LOADINGSCREEN", StringComparison.Ordinal),
                log.LastIndexOf("newState=cClientRunStateLoggingIn", StringComparison.Ordinal)),
            log.LastIndexOf("WaitForWorldReady:", StringComparison.Ordinal));
        var inGame = LastInGameMarker(log);

        if (inGame > title && inGame > loading) return ClientStage.InGame;
        if (loading > title) return ClientStage.LoadingWorld;
        if (title >= 0) return ClientStage.TitleScreen;
        return ClientStage.Starting;
    }

    internal static int LastInGameMarker(string log) => Math.Max(
        log.LastIndexOf("newState=GAMESTATE_INGAME", StringComparison.Ordinal),
        log.LastIndexOf("newState=cClientRunStateRunning", StringComparison.Ordinal));

    internal static bool WasAuthenticationRejected(string log) =>
        log.Contains("Unable to authenticate with Login Server.", StringComparison.Ordinal);

    /// <summary>Le client attend une nouvelle session après une sortie de match : il faut un ticket frais.
    /// Vrai seulement si le marqueur est POSTÉRIEUR au dernier « en jeu » (pas un vieux résidu).</summary>
    internal static bool NeedsReloginTicket(string log, out int position)
    {
        position = log.LastIndexOf("WaitingForReloginSession", StringComparison.Ordinal);
        return position >= 0 && position > LastInGameMarker(log);
    }

    /// <summary>Fin du journal vivant (512 Ko max) + son décalage absolu. Vide si le journal est celui
    /// d'une session précédente (antérieur au lancement) ou en cours d'écriture.</summary>
    internal static (string Text, long Offset) ReadLiveLogTail(DateTimeOffset notBefore)
    {
        try
        {
            var info = new FileInfo(LiveLogPath);
            if (!info.Exists || info.LastWriteTimeUtc < notBefore.UtcDateTime.AddSeconds(-2)) return (string.Empty, 0);
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            var start = Math.Max(0, length - LogTailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return (reader.ReadToEnd(), start);
        }
        catch (IOException)
        {
            return (string.Empty, 0);
        }
    }

    // ------------------------------------------------------------------ diagnostics d'un échec

    public static string? CaptureAttemptDiagnostics(string operationId, int attempt, string summary)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DiagnosticsDir);
            var dir = Path.Combine(AppPaths.DiagnosticsDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{operationId}-a{attempt}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "attempt.txt"), summary);
            foreach (var name in new[] { "H1Z1.log", "H1Z1 PlayClient (Live).log", "Login.log", "NetInfo.log" })
            {
                var source = Path.Combine(LogsDir, name);
                if (!File.Exists(source)) continue;
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = new FileStream(Path.Combine(dir, name), FileMode.Create, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }

            foreach (var old in new DirectoryInfo(AppPaths.DiagnosticsDir).GetDirectories()
                         .OrderByDescending(d => d.CreationTimeUtc).Skip(5))
                try { old.Delete(recursive: true); } catch { }
            return dir;
        }
        catch (Exception ex)
        {
            Log.Warn("diagnostic de tentative non enregistré", ex);
            return null;
        }
    }
}
