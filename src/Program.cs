using System.Diagnostics;
using System.Text;

namespace COTK.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && string.Equals(args[0], "--cleanup-updater", StringComparison.Ordinal))
        {
            try { Directory.Delete(Path.GetDirectoryName(args[1])!, recursive: true); } catch { }
            args = [];
        }
        if (args.Length == 2 && string.Equals(args[0], "--apply-launcher-update", StringComparison.Ordinal))
            return UpdateService.ApplyLauncherUpdateAsync(args[1]).GetAwaiter().GetResult();

        LauncherSettings.Load();

        ApplicationConfiguration.Initialize();

        // A second launcher could overwrite a fresh game ticket in ClientConfig.ini.
        using var single = new Mutex(true, @"Local\COTK.Launcher.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("Le launcher COTK est déjà ouvert.", "COTK");
            return 0;
        }

        try
        {
            using var api = new AuthApiClient();
            Application.Run(new MainForm(api));
            return 0;
        }
        catch (InvalidOperationException)
        {
            MessageBox.Show("La configuration du service COTK est invalide.", "COTK");
            return 1;
        }
    }
}

internal static class GameLauncher
{
    internal enum ClientStage
    {
        Starting,
        TitleScreen,
        LoadingWorld,
        InGame,
    }

    internal enum AttemptOutcome
    {
        InGame,
        ExitedBeforeMenu,
        ClosedAtMenu,
        ExitedWhileLoading,
        StartupTimedOut,
        LoadingTimedOut,
        CrashedAfterInGame,
        AuthenticationRejected,
    }

    internal sealed record AttemptResult(
        AttemptOutcome Outcome,
        ClientStage Stage,
        int ProcessId,
        int? ExitCode,
        TimeSpan Elapsed)
    {
        // Une fois InGame atteint, une sortie est une fermeture volontaire du joueur :
        // plus jamais de relance automatique (CrashedAfterInGame exclu du retry).
        public bool ShouldRetry => Outcome is AttemptOutcome.ExitedBeforeMenu
            or AttemptOutcome.ExitedWhileLoading
            or AttemptOutcome.StartupTimedOut
            or AttemptOutcome.LoadingTimedOut;
    }

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan LoadingTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MenuWaitTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InGameStabilityWindow = TimeSpan.FromMinutes(3);

    public static string RepoRoot
    {
        get
        {
            // Racine = ancetre contenant README.md + les dossiers client et server.
            // Un simple README.md ne suffit pas : le launcher vit dans launcher\
            // qui porte aussi son propre README.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(dir.FullName, "client"))
                    && Directory.Exists(Path.Combine(dir.FullName, "server")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "README.md")))
                dir = dir.Parent;
            return dir?.FullName ?? AppContext.BaseDirectory;
        }
    }

    public static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(RepoRoot, "launcher", "data");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "launcher.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static string ClientDir => LauncherSettings.ClientDirectory ?? Path.Combine(RepoRoot, "client");

    /// <summary>Change l'emplacement du client et le persiste dans %APPDATA%.</summary>
    public static void SetClientDir(string path)
    {
        LauncherSettings.SetClientDirectory(path);
        Log($"client directory set to {path}");
    }
    public static string ServerDir => Path.Combine(RepoRoot, "server", "H1Z1-2017-CSharp-Server");
    private static string ClientConfig => Path.Combine(ClientDir, "ClientConfig.ini");

    public static bool IsGameRunning()
    {
        var expected = Path.GetFullPath(Path.Combine(ClientDir, "H1Z1.exe"));
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

    public static void WriteGameTicket(GameTicket ticket)
    {
        if (ticket.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(10))
            throw new InvalidOperationException("The game ticket expires too soon.");

        var gameExe = Path.Combine(ClientDir, "H1Z1.exe");
        if (!File.Exists(gameExe))
            throw new FileNotFoundException("H1Z1.exe is missing.", gameExe);

            if (!File.Exists(ClientConfig))
            {
                var example = Path.Combine(ServerDir, "ClientConfig.example.ini");
                if (!File.Exists(example))
                {
                    // Installation portable (joueurs) : template embarque a cote de l'exe.
                    example = Path.Combine(AppContext.BaseDirectory, "ClientConfig.example.ini");
                    if (!File.Exists(example))
                        throw new FileNotFoundException("Client configuration template missing.", example);
                }
                File.Copy(example, ClientConfig, overwrite: true);
            }

        var lines = BuildClientConfig(File.ReadAllLines(ClientConfig), ticket.Value);
        var temporary = $"{ClientConfig}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
            File.Move(temporary, ClientConfig, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
        Log($"Fresh game ticket committed atomically; expires={ticket.ExpiresAt:O}");
    }

    internal static List<string> BuildClientConfig(IEnumerable<string> source, string ticket)
    {
        var lines = source
            .Where(line => !line.TrimStart().StartsWith("SessionId=", StringComparison.OrdinalIgnoreCase)
                && !line.TrimStart().StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        lines.Insert(0, "Server=" + LauncherConfig.GameServer);
        lines.Insert(0, $"SessionId={ticket}");
        return lines;
    }

    public static bool ServerPortsUp()
    {
        // Serveur distant : pas de sonde UDP fiable depuis ici. On verifie
        // l'API (meme machine que le serveur de jeu) comme indicateur de vie.
        if (!LauncherConfig.GameServerIsLocal)
            return ApiReachable();

        var ports = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveUdpListeners()
            .Select(endpoint => endpoint.Port)
            .ToHashSet();
        return ports.Contains(20042) && ports.Contains(60000);
    }

    private static bool ApiReachable()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = http.GetAsync(LauncherConfig.ApiUrl.TrimEnd('/') + "/healthz").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static Process StartGame(int attempt, string operationId)
    {
        if (IsGameRunning())
            throw new InvalidOperationException("H1Z1 is already running.");

        Log($"op={operationId} attempt={attempt} phase=start");
        return Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(ClientDir, "H1Z1.exe"),
            WorkingDirectory = ClientDir,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("H1Z1.exe did not return a process.");
    }

    public static async Task<AttemptResult> MonitorGameAsync(
        Process process,
        int attempt,
        string operationId,
        Action<ClientStage> stageChanged,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stage = ClientStage.Starting;
        DateTimeOffset? loadingStartedAt = null;
        DateTimeOffset? firstWorldReadyAt = null;
        var liveLog = Path.Combine(ClientDir, "Logs", "H1Z1 PlayClient (Live).log");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptLog = ReadCurrentAttemptLog(liveLog, startedAt);
            process.Refresh();
            if (process.HasExited)
            {
                var outcome = WasAuthenticationRejected(attemptLog)
                    ? AttemptOutcome.AuthenticationRejected
                    : stage switch
                {
                    ClientStage.TitleScreen => AttemptOutcome.ClosedAtMenu,
                    ClientStage.LoadingWorld => AttemptOutcome.ExitedWhileLoading,
                    ClientStage.InGame => AttemptOutcome.CrashedAfterInGame,
                    _ => AttemptOutcome.ExitedBeforeMenu,
                };
                var result = new AttemptResult(outcome, stage, process.Id, process.ExitCode, DateTimeOffset.UtcNow - startedAt);
                LogAttemptResult(operationId, attempt, result);
                CaptureAttemptDiagnostics(operationId, attempt, result);
                return result;
            }

            var detected = DetectClientStage(attemptLog);
            if (detected != stage)
            {
                stage = detected;
                if (stage == ClientStage.LoadingWorld)
                    loadingStartedAt = DateTimeOffset.UtcNow;
                else
                    loadingStartedAt = null;
                if (stage == ClientStage.InGame && !firstWorldReadyAt.HasValue)
                    firstWorldReadyAt = DateTimeOffset.UtcNow;
                stageChanged(stage);
                Log($"op={operationId} attempt={attempt} pid={process.Id} stage={stage}");
            }

            if (stage == ClientStage.InGame
                && firstWorldReadyAt.HasValue
                && DateTimeOffset.UtcNow - firstWorldReadyAt.Value >= InGameStabilityWindow)
            {
                var result = new AttemptResult(AttemptOutcome.InGame, stage, process.Id, null, DateTimeOffset.UtcNow - startedAt);
                LogAttemptResult(operationId, attempt, result);
                return result;
            }

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            if (stage == ClientStage.Starting && elapsed >= StartupTimeout)
                return await StopTimedOutAttemptAsync(process, attempt, operationId, stage, AttemptOutcome.StartupTimedOut, startedAt);

            if (stage == ClientStage.LoadingWorld
                && loadingStartedAt.HasValue
                && DateTimeOffset.UtcNow - loadingStartedAt.Value >= LoadingTimeout)
                return await StopTimedOutAttemptAsync(process, attempt, operationId, stage, AttemptOutcome.LoadingTimedOut, startedAt);

            if (stage == ClientStage.TitleScreen && elapsed >= MenuWaitTimeout)
            {
                var result = new AttemptResult(AttemptOutcome.ClosedAtMenu, stage, process.Id, null, elapsed);
                LogAttemptResult(operationId, attempt, result);
                return result;
            }

            await Task.Delay(500, cancellationToken);
        }
    }

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
        var inGame = Math.Max(
            log.LastIndexOf("newState=GAMESTATE_INGAME", StringComparison.Ordinal),
            log.LastIndexOf("newState=cClientRunStateRunning", StringComparison.Ordinal));

        if (inGame > title && inGame > loading) return ClientStage.InGame;
        if (loading > title) return ClientStage.LoadingWorld;
        if (title >= 0) return ClientStage.TitleScreen;
        return ClientStage.Starting;
    }

    internal static bool WasAuthenticationRejected(string log) =>
        log.Contains("Unable to authenticate with Login Server.", StringComparison.Ordinal);

    public static void StartToggleConsole(LauncherAccount account, Process gameProcess, string operationId)
    {
        if (!account.IsAdmin)
        {
            Log($"op={operationId} ToggleConsole skipped for non-admin role");
            return;
        }

        try
        {
            gameProcess.Refresh();
            if (gameProcess.HasExited) return;
            foreach (var stale in Process.GetProcessesByName("ToggleConsole"))
            {
                try { stale.Kill(); }
                catch { }
                finally { stale.Dispose(); }
            }
            var toggleExe = Path.Combine(ServerDir, "ToggleConsole", "ToggleConsole.exe");
            if (!File.Exists(toggleExe))
            {
                Log($"op={operationId} ToggleConsole missing");
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = toggleExe,
                Arguments = "--watch --key 77",
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Path.GetDirectoryName(toggleExe)!,
            });
            Log($"op={operationId} ToggleConsole started after GAMESTATE_INGAME");
        }
        catch (Exception ex)
        {
            Log($"op={operationId} ToggleConsole failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string ReadCurrentAttemptLog(string path, DateTimeOffset startedAt)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.LastWriteTimeUtc < startedAt.UtcDateTime.AddSeconds(-2))
                return string.Empty;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static async Task<AttemptResult> StopTimedOutAttemptAsync(
        Process process,
        int attempt,
        string operationId,
        ClientStage stage,
        AttemptOutcome outcome,
        DateTimeOffset startedAt)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        catch { }
        int? exitCode = process.HasExited ? process.ExitCode : null;
        var result = new AttemptResult(outcome, stage, process.Id, exitCode, DateTimeOffset.UtcNow - startedAt);
        LogAttemptResult(operationId, attempt, result);
        CaptureAttemptDiagnostics(operationId, attempt, result);
        return result;
    }

    private static void LogAttemptResult(string operationId, int attempt, AttemptResult result)
    {
        var exit = result.ExitCode.HasValue ? $"0x{unchecked((uint)result.ExitCode.Value):X8}" : "n/a";
        Log($"op={operationId} attempt={attempt} pid={result.ProcessId} outcome={result.Outcome} "
            + $"stage={result.Stage} elapsed={result.Elapsed.TotalSeconds:F1}s exit={exit}");
    }

    private static void CaptureAttemptDiagnostics(string operationId, int attempt, AttemptResult result)
    {
        try
        {
            var root = Path.Combine(RepoRoot, "launcher", "data", "diagnostics");
            Directory.CreateDirectory(root);
            var dir = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{operationId}-a{attempt}");
            Directory.CreateDirectory(dir);
            var exit = result.ExitCode.HasValue ? $"0x{unchecked((uint)result.ExitCode.Value):X8}" : "n/a";
            File.WriteAllText(Path.Combine(dir, "attempt.txt"),
                $"outcome={result.Outcome}{Environment.NewLine}stage={result.Stage}{Environment.NewLine}"
                + $"pid={result.ProcessId}{Environment.NewLine}exit={exit}{Environment.NewLine}"
                + $"elapsedSeconds={result.Elapsed.TotalSeconds:F1}{Environment.NewLine}");
            foreach (var name in new[] { "H1Z1.log", "H1Z1 PlayClient (Live).log", "Login.log", "NetInfo.log" })
                CopySharedFile(Path.Combine(ClientDir, "Logs", name), Path.Combine(dir, name));

            foreach (var old in new DirectoryInfo(root).GetDirectories().OrderByDescending(item => item.CreationTimeUtc).Skip(5))
                try { old.Delete(recursive: true); } catch { }
        }
        catch { }
    }

    private static void CopySharedFile(string source, string destination)
    {
        if (!File.Exists(source)) return;
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }
}
