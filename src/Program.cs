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
    // 2026-09-14: 20 s au lieu de 3 min. L'ancienne valeur datait de l'ère du
    // patch mémoire (attacher en plein chargement a crashé un client) ; l'attache
    // native est en lecture seule jusqu'au premier F8, donc sans risque. La marge
    // restante évite d'armer la surveillance sur un InGame transitoire du menu.
    // Implémenté : reset à la sortie d'InGame (ci-dessous) + re-arm post-game
    // (EnsureToggleWatcher) ; l'outcome, lui, n'est déclaré qu'une fois.
    // Sémantique : délai après InGame stable de l'épisode courant (re-armé à
    // chaque épisode) ; l'outcome, lui, n'est déclaré qu'une fois. Les cas
    // chargement lent et nouvelle session post-Exit exigent une validation live.
    private static readonly TimeSpan InGameStabilityWindow = TimeSpan.FromSeconds(20);

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
        EnforceClientLogLevel(lines);
        lines.Insert(0, "Server=" + LauncherConfig.GameServer);
        lines.Insert(0, $"SessionId={ticket}");
        return lines;
    }

    /// <summary>
    /// Client log level written on every launch (2026-09-27, stutter root cause).
    /// At LocalLogLevel=9 the 1087 client writes one line per render pipeline it creates
    /// (~3800 per session) synchronously on the frame thread, reopening the log file each
    /// time: 40-110 ms hitches when driving through towns, worst on HDDs. Measured on a real
    /// client: same town, same session, 21 hitches at level 9, none at level 3. Level 4, not 3:
    /// the stage markers DetectClientStage reads (GAMESTATE_*, cClientRunStateRunning,
    /// BaseApp::Run, WaitForWorldReady) are level-4 lines, and none are written while driving;
    /// the pipeline spam is level 5. The key must be present: absent, the client defaults to 5.
    /// COTK_CLIENT_LOG_LEVEL overrides it for debugging.
    /// </summary>
    internal const int DefaultClientLogLevel = 4;

    internal static int ClientLogLevel =>
        int.TryParse(Environment.GetEnvironmentVariable("COTK_CLIENT_LOG_LEVEL"), out var level)
            && level is >= 0 and <= 9
                ? level
                : DefaultClientLogLevel;

    /// <summary>Exactly one LocalLogLevel line, right under the first [Logging] header
    /// (the section is appended when missing). Other lines are left untouched.</summary>
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

        if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            lines.Add(string.Empty);
        lines.Add("[Logging]");
        lines.Add(entry);
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
        DateTimeOffset? lastLoadingLogAt = null;
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

            // Ne jamais régresser sur un log vide/tronqué (fichier en cours d'écriture, IO partagé).
            // Un log vide signifie "pas d'info", pas "retour à Starting".
            if (!string.IsNullOrEmpty(attemptLog))
            {
                var detected = DetectClientStage(attemptLog);
                if (detected != stage)
                {
                    // Éviter le flapping Starting<->LoadingWorld sur lecture partielle: exiger que
                    // la régression vers Starting ne se fasse que si le log contient explicitement
                    // un marqueur plus récent; un log vide est déjà filtré ci-dessus.
                    stage = detected;
                    if (stage == ClientStage.LoadingWorld)
                    {
                        loadingStartedAt ??= DateTimeOffset.UtcNow;
                        lastLoadingLogAt = DateTimeOffset.UtcNow;
                    }
                    else
                    {
                        loadingStartedAt = null;
                        lastLoadingLogAt = null;
                        // 2026-09-14: per-episode arming — leaving InGame clears the
                        // timer so a later episode (slow load, post-Exit relogin)
                        // gets its own stability window instead of losing F8 forever.
                        firstWorldReadyAt = null;
                    }
                    if (stage == GameLauncher.ClientStage.InGame && !firstWorldReadyAt.HasValue)
                        firstWorldReadyAt = DateTimeOffset.UtcNow;
                    stageChanged(stage);
                    Log($"op={operationId} attempt={attempt} pid={process.Id} stage={stage} logLen={attemptLog.Length}");
                }
                else if (stage == ClientStage.LoadingWorld)
                {
                    lastLoadingLogAt = DateTimeOffset.UtcNow;
                }
            }

            // Observabilité loading: log périodique si bloqué >30s sans progression InGame.
            if (stage == ClientStage.LoadingWorld
                && loadingStartedAt.HasValue
                && lastLoadingLogAt.HasValue
                && DateTimeOffset.UtcNow - loadingStartedAt.Value >= TimeSpan.FromSeconds(30)
                && DateTimeOffset.UtcNow - lastLoadingLogAt.Value >= TimeSpan.FromSeconds(15))
            {
                var stall = DateTimeOffset.UtcNow - loadingStartedAt.Value;
                Log($"op={operationId} attempt={attempt} pid={process.Id} stage=LoadingWorld STALL {stall.TotalSeconds:F0}s — vérifiez server.log [READY] et client Logs/H1Z1 PlayClient (Live).log WaitForWorldReady");
                lastLoadingLogAt = DateTimeOffset.UtcNow;
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

    /// <summary>2026-09-14: résolution du payload console native. Layout installé
    /// d'abord (à côté du launcher) ; l'ancienne copie ServerDir UNIQUEMENT derrière
    /// le marqueur explicite COTK_TOGGLECONSOLE_DEV=1, pour qu'une copie stale ne soit
    /// jamais retrouvée silencieusement. Retourne null + log explicite si absent.</summary>
    internal static string? ResolveToggleExe(string operationId)
    {
        string installed = Path.Combine(AppContext.BaseDirectory, "ToggleConsole", "ToggleConsole.exe");
        if (File.Exists(installed))
        {
            return installed;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("COTK_TOGGLECONSOLE_DEV"), "1", StringComparison.Ordinal))
        {
            string legacy = Path.Combine(ServerDir, "ToggleConsole", "ToggleConsole.exe");
            if (File.Exists(legacy))
            {
                Log($"op={operationId} ToggleConsole dev fallback (COTK_TOGGLECONSOLE_DEV=1): {legacy}");
                return legacy;
            }
        }

        Log($"op={operationId} ToggleConsole missing (looked beside the launcher; no dev fallback without COTK_TOGGLECONSOLE_DEV=1) — console unavailable, game unaffected");
        return null;
    }

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
            string? toggleExe = ResolveToggleExe(operationId);
            if (toggleExe is null)
            {
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = toggleExe,
                // 2026-09-14: native UI mode only — NEVER --patch here. The old
                // in-memory patches trip the client's integrity check: c0000005
                // at 0x140C06373 on the next zone load (proof: COTK
                // out/exit-analysis-20260914.md). --native-ui opens the
                // console through native UI events and writes no client code.
                Arguments = "--native-ui --watch --key 77",
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
