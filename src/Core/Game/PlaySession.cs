using System.Diagnostics;
using System.Net.NetworkInformation;

namespace COTK.Next.Core;

internal enum PlayPhase { CheckingServer, PreparingTicket, Starting, TitleScreen, Loading, InGame, Closed }

internal sealed record PlayStatus(PlayPhase Phase, string Text, int Attempt = 1, int MaxAttempts = 1);

internal enum AttemptOutcome
{
    ClosedAfterGame,
    ExitedBeforeMenu,
    ClosedAtMenu,
    ExitedWhileLoading,
    StartupTimedOut,
    LoadingTimedOut,
    CrashedInGame,
    AuthenticationRejected,
}

internal sealed record AttemptResult(AttemptOutcome Outcome, ClientStage Stage, int ProcessId, int? ExitCode, TimeSpan Elapsed, bool ReachedStableGame)
{
    // Une fois le jeu stable, une sortie est une fermeture volontaire : jamais de relance automatique.
    public bool ShouldRetry => !ReachedStableGame && Outcome is AttemptOutcome.ExitedBeforeMenu
        or AttemptOutcome.ExitedWhileLoading or AttemptOutcome.StartupTimedOut or AttemptOutcome.LoadingTimedOut;

    public string ExitHex => ExitCode is { } c ? $"0x{unchecked((uint)c):X8}" : "n/a";
}

/// <summary>Cycle de vie d'une partie : serveur → ticket → H1Z1.exe → surveillance (menu, chargement, jeu)
/// → nouveaux tickets à chaque retour au lobby, console F8 pour le staff. Un seul surveillant qui dure
/// tant que le jeu tourne (l'ancien launcher en avait deux, dont un s'arrêtait après 10 min au menu).</summary>
internal sealed class PlaySession
{
    public const int MaxAttempts = 8;

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan LoadingTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan InGameStabilityWindow = TimeSpan.FromSeconds(20);

    private readonly ApiClient _api;
    private readonly Session _session;
    private readonly Action<PlayStatus> _status;

    public PlaySession(ApiClient api, Session session, Action<PlayStatus> status)
    {
        _api = api;
        _session = session;
        _status = status;
    }

    /// <summary>Retourne null si tout s'est bien passé (jeu joué puis fermé), sinon l'erreur à afficher.</summary>
    public async Task<LauncherError?> RunAsync(CancellationToken ct)
    {
        var operationId = Guid.NewGuid().ToString("N")[..8];
        Log.Info($"op={operationId} lancement demandé");

        if (GameFiles.IsGameRunning()) return Errors.AlreadyRunning();
        if (!File.Exists(GameFiles.ExePath)) return Errors.ExeMissing(GameFiles.ExePath);
        try { LaunchPreflight.CheckClient(GameFiles.ClientDir,AppPaths.InstallDir); }
        catch(Exception ex) { return Errors.From(ex); }

        _status(new PlayStatus(PlayPhase.CheckingServer, "Vérification du serveur…"));
        if (!await ServerReachableAsync(ct)) return Errors.ServerDown();

        AttemptResult? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            _status(new PlayStatus(PlayPhase.PreparingTicket, "Préparation de votre session…", attempt, MaxAttempts));

            GameTicket ticket;
            try
            {
                ticket = await _api.CreateGameTicketAsync(_session.AccessToken, ct);
                GameFiles.WriteGameTicket(ticket);
            }
            catch (LauncherException ex)
            {
                return ex.Error;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Errors.From(ex);
            }

            Process game;
            try
            {
                Log.Info($"op={operationId} attempt={attempt} phase=start");
                game = Process.Start(new ProcessStartInfo
                {
                    FileName = GameFiles.ExePath,
                    WorkingDirectory = GameFiles.ClientDir,
                    UseShellExecute = false,
                }) ?? throw new InvalidOperationException("H1Z1.exe n'a pas renvoyé de processus.");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Log.Error($"op={operationId} démarrage impossible", ex);
                return Errors.LaunchFailed(ex.Message);
            }

            using (game)
            {
                last = await MonitorAsync(game, attempt, operationId, ct);
            }

            if (last.ReachedStableGame || last.Outcome == AttemptOutcome.ClosedAfterGame)
            {
                _status(new PlayStatus(PlayPhase.Closed, "Partie terminée.", attempt, MaxAttempts));
                return null;
            }
            if (last.Outcome == AttemptOutcome.AuthenticationRejected) return Errors.TicketRejected();
            if (!last.ShouldRetry)
            {
                // Fermé volontairement depuis le menu : pas une erreur.
                _status(new PlayStatus(PlayPhase.Closed, "Jeu fermé.", attempt, MaxAttempts));
                return null;
            }

            if (attempt < MaxAttempts)
            {
                _status(new PlayStatus(PlayPhase.Starting,
                    $"Le client a rencontré un problème — nouvelle tentative {attempt + 1}/{MaxAttempts}…", attempt, MaxAttempts));
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        return last?.Outcome switch
        {
            AttemptOutcome.LoadingTimedOut or AttemptOutcome.ExitedWhileLoading when last.ExitCode is null or 0 => Errors.LoadTimeout(),
            _ => Errors.ClientCrashedAtStart(MaxAttempts, last?.ExitHex),
        };
    }

    // ------------------------------------------------------------------ serveur

    private async Task<bool> ServerReachableAsync(CancellationToken ct)
    {
        if (Config.IsLocalGameServer)
        {
            var ports = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Select(e => e.Port).ToHashSet();
            return ports.Contains(20042) && ports.Contains(60000);
        }
        var status=await _api.GetGameStatusAsync(ct);
        if(status is {Maintenance:true}) throw new LauncherException(new LauncherError("E-GAME-010","Maintenance en cours","Le serveur de jeu est en maintenance. Réessayez plus tard.",Retryable:true));
        return status?.CanPlay==true;
    }

    // ------------------------------------------------------------------ surveillance

    private async Task<AttemptResult> MonitorAsync(Process process, int attempt, string operationId, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stage = ClientStage.Starting;
        DateTimeOffset? loadingSince = null;
        DateTimeOffset? inGameSince = null;
        var stable = false;
        var consoleStarted = false;
        var clientPatched = false;
        long lastReloginPosition = -1;

        _status(new PlayStatus(PlayPhase.Starting, "Démarrage du client…", attempt, MaxAttempts));

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var (log, offset) = GameFiles.ReadLiveLogTail(startedAt);
            process.Refresh();

            if (process.HasExited)
            {
                var outcome = GameFiles.WasAuthenticationRejected(log)
                    ? AttemptOutcome.AuthenticationRejected
                    : stable
                        ? AttemptOutcome.ClosedAfterGame
                        : stage switch
                        {
                            ClientStage.TitleScreen => AttemptOutcome.ClosedAtMenu,
                            ClientStage.LoadingWorld => AttemptOutcome.ExitedWhileLoading,
                            ClientStage.InGame => AttemptOutcome.CrashedInGame,
                            _ => AttemptOutcome.ExitedBeforeMenu,
                        };
                var result = new AttemptResult(outcome, stage, process.Id, process.ExitCode, DateTimeOffset.UtcNow - startedAt, stable);
                Log.Info($"op={operationId} attempt={attempt} pid={result.ProcessId} outcome={outcome} stage={stage} elapsed={result.Elapsed.TotalSeconds:F1}s exit={result.ExitHex}");
                if (!stable && outcome != AttemptOutcome.ClosedAtMenu)
                    GameFiles.CaptureAttemptDiagnostics(operationId, attempt,
                        $"outcome={outcome}{Environment.NewLine}stage={stage}{Environment.NewLine}exit={result.ExitHex}{Environment.NewLine}elapsed={result.Elapsed.TotalSeconds:F1}s{Environment.NewLine}");
                return result;
            }

            // Un journal vide veut dire « pas d'information », jamais « retour à Starting ».
            if (log.Length > 0)
            {
                var detected = GameFiles.DetectClientStage(log);
                if (detected != stage)
                {
                    stage = detected;
                    loadingSince = stage == ClientStage.LoadingWorld ? (loadingSince ?? DateTimeOffset.UtcNow) : null;
                    // Un nouvel épisode « en jeu » (chargement lent, retour au lobby) arme sa propre fenêtre de stabilité.
                    inGameSince = stage == ClientStage.InGame ? DateTimeOffset.UtcNow : null;
                    Log.Info($"op={operationId} attempt={attempt} pid={process.Id} stage={stage}");
                    Publish(stage, attempt);

                    // Une fois le client chargé (menu ou plus loin), on installe en mémoire le correctif F pendant
                    // un rechargement. Jamais bloquant : toute garde qui échoue laisse le jeu intact.
                    if (!clientPatched && stage != ClientStage.Starting)
                    {
                        clientPatched = true;
                        await ApplyClientPatchAsync(process, operationId);
                    }
                }

                // Retour au lobby après un match : le ticket de 60 s est périmé, on en distribue un frais.
                if (stage == ClientStage.LoadingWorld && GameFiles.NeedsReloginTicket(log, out var position)
                    && offset + position > lastReloginPosition)
                {
                    lastReloginPosition = offset + position;
                    await RefreshTicketAsync(operationId, ct);
                }
            }

            var now = DateTimeOffset.UtcNow;
            if (stage == ClientStage.InGame && inGameSince is { } since && now - since >= InGameStabilityWindow)
            {
                if (!stable)
                {
                    stable = true;
                    Log.Info($"op={operationId} attempt={attempt} jeu stable");
                }
                if (!consoleStarted || !StaffConsole.IsWatcherRunning())
                {
                    consoleStarted = true;
                    StaffConsole.StartWatcher(_session.Account, process, operationId);
                }
            }

            if (stage == ClientStage.Starting && now - startedAt >= StartupTimeout)
                return await StopAsync(process, attempt, operationId, stage, AttemptOutcome.StartupTimedOut, startedAt);
            // Après un premier « en jeu » stable, un chargement long (zone, lobby) n'est jamais tué.
            if (stage == ClientStage.LoadingWorld && !stable && loadingSince is { } l && now - l >= LoadingTimeout)
                return await StopAsync(process, attempt, operationId, stage, AttemptOutcome.LoadingTimedOut, startedAt);

            await Task.Delay(750, ct);
        }
    }

    private static async Task ApplyClientPatchAsync(Process process, string operationId)
    {
        try
        {
            var result = await Task.Run(() => ClientReloadLootPatch.TryApply(process, GameFiles.ExePath));
            Log.Info($"op={operationId} correctif client (F pendant rechargement) : {result.Outcome} - {result.Detail}");
        }
        catch (Exception ex)
        {
            Log.Warn($"op={operationId} correctif client ignoré", ex);
        }
    }

    private void Publish(ClientStage stage, int attempt)
    {
        var (phase, text) = stage switch
        {
            ClientStage.TitleScreen => (PlayPhase.TitleScreen, "Client prêt — choisissez « Enter Game »."),
            ClientStage.LoadingWorld => (PlayPhase.Loading, "Chargement du monde…"),
            ClientStage.InGame => (PlayPhase.InGame, "En jeu. Bon combat !"),
            _ => (PlayPhase.Starting, "Démarrage du client…"),
        };
        _status(new PlayStatus(phase, text, attempt, MaxAttempts));
    }

    private async Task RefreshTicketAsync(string operationId, CancellationToken ct)
    {
        try
        {
            Log.Info($"op={operationId} retour au lobby détecté, nouveau ticket");
            var fresh = await _api.CreateGameTicketAsync(_session.AccessToken, ct);
            GameFiles.WriteGameTicket(fresh);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.Warn($"op={operationId} ticket de relogin indisponible", ex);
        }
    }

    private static async Task<AttemptResult> StopAsync(
        Process process, int attempt, string operationId, ClientStage stage, AttemptOutcome outcome, DateTimeOffset startedAt)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        catch (Exception ex) { Log.Warn("arrêt du client impossible", ex); }
        int? exit = process.HasExited ? process.ExitCode : null;
        var result = new AttemptResult(outcome, stage, process.Id, exit, DateTimeOffset.UtcNow - startedAt, false);
        Log.Warn($"op={operationId} attempt={attempt} pid={result.ProcessId} outcome={outcome} stage={stage} elapsed={result.Elapsed.TotalSeconds:F1}s");
        GameFiles.CaptureAttemptDiagnostics(operationId, attempt, $"outcome={outcome}{Environment.NewLine}stage={stage}{Environment.NewLine}");
        return result;
    }
}
