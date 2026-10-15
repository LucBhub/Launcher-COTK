using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using COTK.Next.Core;

namespace COTK.Next;

internal enum ClientState { Unknown, Checking, NotInstalled, UpdateAvailable, Ready }

internal enum Operation { None, Installing, Verifying, Repairing, Launching, Playing, Uninstalling }

internal enum LoadStatus { Idle, Loading, Ready, Failed }

/// <summary>Console F8 du staff : Hidden = joueur (rien n'est proposé, rien n'est demandé à l'API).</summary>
internal enum StaffConsoleStatus { Hidden, Checking, UpToDate, NeedsInstall, NeedsUpdate, Installing, Unavailable }

/// <summary>Une donnée distante avec son état : l'UI affiche squelette, contenu, ou erreur avec « Réessayer ».</summary>
internal sealed class Remote<T>
{
    public LoadStatus Status { get; internal set; } = LoadStatus.Idle;
    public T? Value { get; internal set; }
    public LauncherError? Error { get; internal set; }
}

/// <summary>Services d'interface dont le noyau a besoin (implémentés par la fenêtre principale).</summary>
internal interface IShell
{
    Dispatcher Dispatcher { get; }
    void Toast(UI.ToastKind kind, string title, string? message = null, string? code = null, Action? action = null, string? actionLabel = null);
    void ShowError(LauncherError error, Action? retry);
    Task<bool> Confirm(string title, string message, string confirm, string cancel = "Annuler", string? detail = null, string icon = UI.Icons.Download);
    Task<string?> PickFolder(string description);
    void SetWindowMinimized(bool minimized);
}

/// <summary>Cerveau du launcher : session, profil, état du client, opérations longues (installer, vérifier,
/// jouer). Toute la logique métier vit ici ou dans Core ; les vues ne font qu'afficher et réagir à
/// <see cref="Changed"/>. Les travaux lourds (hachage, téléchargement, surveillance du jeu) tournent
/// hors du thread d'interface.</summary>
internal sealed class LauncherApp : IDisposable
{
    private readonly ClientInstaller _installer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private IShell _shell = null!;

    public ApiClient Api { get; } = new();

    public bool Preview { get; }

    public Session? Session { get; private set; }
    public bool Restoring { get; private set; } = true;
    public string? SignOutReason { get; private set; }

    public Remote<Profile> Profile { get; } = new();
    public Remote<List<PatchNote>> Notes { get; } = new();
    public Remote<Leaderboard> Board { get; } = new();

    public int? PingMs { get; private set; }
    public bool? ServerOnline { get; private set; }
    public GameServiceStatus? GameStatus { get; private set; }

    public UpdateManifest? Manifest { get; private set; }
    public ClientState ClientState { get; private set; } = ClientState.Unknown;
    public string? InstalledVersion { get; private set; }
    public string? LatestVersion => Manifest?.Client.Version;
    public long ClientDownloadSize => Manifest?.Client.SizeBytes ?? 0;

    public StaffConsoleStatus StaffConsoleStatus { get; private set; }
    private StaffConsoleInfo? _consoleInfo;

    /// <summary>Nouvelle version du launcher publiée (null = à jour).</summary>
    public LauncherRelease? LauncherUpdate { get; private set; }
    public bool LauncherUpdating { get; private set; }
    private bool _updatePrompted;

    public Operation Op { get; private set; }
    private TransferProgress? _transfer;
    public TransferProgress? Transfer { get => _installer.TransferControl.Paused && _transfer is {Phase:TransferPhase.Downloading} ? _transfer with {Phase=TransferPhase.Paused,Message="Téléchargement en pause",BytesPerSecond=0,Eta=null} : _transfer; private set=>_transfer=value; }
    public bool DownloadPaused=>_installer.TransferControl.Paused;
    public void ToggleDownloadPause()
    {
        if(Op!=Operation.Installing || _transfer?.Phase!=TransferPhase.Downloading) return;
        if(DownloadPaused)_installer.TransferControl.Resume();else _installer.TransferControl.Pause();
        Raise("transfer");
    }
    public PlayStatus? Play { get; private set; }

    /// <summary>Nom de la propriété modifiée : "session", "profile", "notes", "board", "server", "client", "operation", "transfer", "play".</summary>
    public event Action<string>? Changed;

    private readonly string? _previewScreen;

    public LauncherApp(bool preview, string? previewScreen = null)
    {
        Preview = preview;
        _previewScreen = previewScreen;
    }

    public void Attach(IShell shell)
    {
        _shell = shell;
        _installer.ConfirmStoragePlan = (plan, isUpdate) => OnUi(() => _shell.Confirm(
            isUpdate ? "Mise à jour complète du client" : "Installer le client du jeu",
            isUpdate
                ? "Les fichiers ne peuvent pas être mis à jour un par un. Le client complet doit être retéléchargé."
                : "Le client du jeu doit être téléchargé depuis les serveurs COTK. Vous pouvez suivre et annuler le téléchargement à tout moment.",
            isUpdate ? "Mettre à jour" : "Télécharger",
            detail: $"Dossier : {plan.Directory}\nTéléchargement : {Fmt.Size(plan.DownloadBytes)}\nEspace nécessaire : {Fmt.Size(plan.RequiredBytes)} · Disponible : {Fmt.Size(plan.AvailableBytes)}"));
        _installer.PickFolder = () => OnUi(() => _shell.PickFolder("Où installer le client du jeu ? Un dossier « client » y sera créé."));
    }

    private Task<T> OnUi<T>(Func<Task<T>> work) => _shell.Dispatcher.InvokeAsync(work).Task.Unwrap();

    private void Raise(string what) => _shell.Dispatcher.Invoke(() => Changed?.Invoke(what));

    // ================================================================== démarrage / session

    public async Task StartAsync()
    {
        if (Preview)
        {
            Restoring = false;
            if (_previewScreen is "login" or "password")
            {
                Raise("session");
                return;
            }
            Session = new Session("preview", new Account("IronWolf_92", "user", DateTimeOffset.UtcNow.AddDays(30)));
            Restoring = false;
            SeedPreview();
            Raise("session");
            _ = ServerLoopAsync();
            return;
        }

        _ = ServerLoopAsync();
        string? token;
        try { token = CredentialStore.ReadToken(); }
        catch (Exception ex)
        {
            Log.Warn("lecture du jeton impossible", ex);
            token = null;
            SignOutReason = "Impossible de lire la session enregistrée sur ce PC. Reconnectez-vous.";
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var account = await Api.GetAccountAsync(token, _lifetime.Token);
                if (account.IsStaff) CredentialStore.TryDelete(); // une session staff n'est jamais persistée
                EnterSession(new Session(token, account));
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (LauncherException ex)
            {
                if (ex.Error.Code == "E-AUTH-003" || ex.Error.Code == "E-AUTH-001")
                {
                    CredentialStore.TryDelete();
                    SignOutReason = "Votre session a expiré. Reconnectez-vous.";
                }
                else
                {
                    // Réseau coupé : on garde le jeton, on explique, le joueur peut réessayer.
                    SignOutReason = ex.Error.Message + " " + (ex.Error.Hint ?? "");
                }
                Log.Warn($"restauration de session : {ex.Error.Code}");
            }
        }

        Restoring = false;
        Raise("session");
    }

    private void EnterSession(Session session)
    {
        Session = session;
        Restoring = false;
        SignOutReason = null;
        Log.Info($"connecté : {session.Account.Username} ({session.Account.Role})");
        Raise("session");
        _ = RefreshAllAsync();
        _ = RefreshStaffConsoleAsync();
    }

    public async Task LoginWithPasswordAsync(string username, string password, CancellationToken ct)
    {
        if (Preview) throw new LauncherException(Errors.BadCredentials());
        var session = await Api.LoginAsync(username, password, ct);
        PersistAndEnter(session);
    }

    /// <summary>Connexion par le navigateur (le mot de passe ne passe jamais par le launcher).</summary>
    public async Task LoginWithBrowserAsync(Action<DeviceStart> onCode, CancellationToken ct)
    {
        if (Preview) throw new LauncherException(Errors.Offline("Mode aperçu : aucun appel d'authentification."));
        var start = await Api.StartDeviceAsync(ct);
        onCode(start);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, start.ExpiresIn));
        var interval = TimeSpan.FromSeconds(Math.Max(5, start.Interval));
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(interval, ct);
            var poll = await Api.PollDeviceAsync(start.DeviceCode, ct);
            if (poll.Status != DevicePollStatus.Approved || poll.AccessToken is null) continue;

            var account = await Api.GetAccountAsync(poll.AccessToken, ct);
            PersistAndEnter(new Session(poll.AccessToken, account));
            return;
        }
        throw new LauncherException(Errors.DeviceExpired());
    }

    private void PersistAndEnter(Session session)
    {
        try
        {
            if (session.Account.IsStaff) CredentialStore.TryDelete();
            else CredentialStore.WriteToken(session.AccessToken);
        }
        catch (Exception ex)
        {
            // Connexion valable pour cette fois ; seulement la mémorisation échoue.
            Log.Warn("jeton non mémorisé", ex);
            _shell.Toast(UI.ToastKind.Warning, "Session non mémorisée", "Vous devrez vous reconnecter au prochain lancement.");
        }
        EnterSession(session);
    }

    public async Task LogoutAsync()
    {
        var session = Session;
        if (session is null) return;
        if (!Preview)
        {
            try { await Api.LogoutAsync(session.AccessToken, CancellationToken.None); }
            catch (Exception ex) { Log.Warn("déconnexion serveur impossible (session purgée localement)", ex); }
            CredentialStore.TryDelete();
        }
        SignOut("Session fermée sur cet ordinateur.");
    }

    private void SignOut(string? reason)
    {
        Session = null;
        SignOutReason = reason;
        Profile.Status = LoadStatus.Idle; Profile.Value = default;
        Board.Status = LoadStatus.Idle; Board.Value = default;
        ClientState = ClientState.Unknown;
        StaffConsoleStatus = StaffConsoleStatus.Hidden;
        _consoleInfo = null;
        Raise("session");
    }

    /// <summary>Session invalide en cours de route : retour à l'écran de connexion avec une explication.</summary>
    private void ExpireSession()
    {
        CredentialStore.TryDelete();
        SignOut("Votre session a expiré. Reconnectez-vous.");
    }

    // ================================================================== mise à jour du launcher

    private void CheckLauncherUpdate(UpdateManifest manifest)
    {
        if (Preview) return;
        var release = LauncherUpdater.Check(manifest);
        LauncherUpdate = release;
        Raise("launcherupdate");
        if (release is null || _updatePrompted) return;
        _updatePrompted = true;
        Log.Info($"mise à jour du launcher disponible : {Config.Version} → {release.Version}");

        if (LauncherUpdater.ShouldAutoApply(release))
        {
            // Comme l'ancien launcher : aucune question. Jamais pendant une installation ou une partie (réessayé à l'ouverture suivante).
            if (IsBusy) { Log.Info("mise à jour automatique du launcher reportée : opération en cours"); return; }
            _ = UpdateLauncherAsync(automatic: true);
        }
        else if (Settings.AutoUpdateLauncher)
        {
            // Une tentative automatique vient d'échouer : on reste utilisable, on réessaiera seul dans 24 h.
            _shell.Toast(UI.ToastKind.Warning, "Mise à jour du launcher en attente",
                "La dernière tentative n'a pas abouti. Nouvel essai automatique dans 24 h, ou réessayez maintenant.",
                null, () => _ = UpdateLauncherAsync(), "Réessayer");
        }
        else _ = PromptLauncherUpdateAsync(release); // option désactivée : on demande
    }

    private async Task PromptLauncherUpdateAsync(LauncherRelease release)
    {
        try
        {
            var accepted = await OnUi(() => _shell.Confirm(
                "Mise à jour du launcher",
                "Une nouvelle version du launcher est disponible. Il va se fermer puis se relancer tout seul. Vos réglages et le jeu ne sont pas touchés.",
                "Mettre à jour", "Plus tard",
                detail: $"Version {Config.Version} → {release.Version}\nTéléchargement : {Fmt.Size(release.SizeBytes)}"));
            if (accepted) await UpdateLauncherAsync();
        }
        catch (Exception ex) { Log.Warn("proposition de mise à jour du launcher", ex); }
    }

    /// <summary>Télécharge, vérifie et installe la nouvelle version, puis ferme le launcher : la copie installatrice
    /// remplace les fichiers et relance le nouveau launcher. Refusé pendant une installation ou une partie.</summary>
    public async Task UpdateLauncherAsync(bool automatic = false)
    {
        var release = LauncherUpdate;
        if (release is null || Preview || LauncherUpdating) return;
        if (IsBusy)
        {
            if (!automatic)
                _shell.Toast(UI.ToastKind.Warning, "Terminez d'abord l'opération en cours",
                    "La mise à jour du launcher redémarre l'application. Réessayez quand l'installation ou la partie est terminée.");
            return;
        }

        LauncherUpdating = true;
        Raise("launcherupdate");
        _shell.Toast(UI.ToastKind.Info, "Téléchargement de la mise à jour…", $"Launcher {release.Version}");
        try
        {
            var archive = await LauncherUpdater.DownloadAsync(release, null, _lifetime.Token);
            LauncherUpdater.RecordAttempt(release.Version); // avant : si l'installateur échoue, on ne boucle pas
            LauncherUpdater.Schedule(archive);
            _shell.Toast(UI.ToastKind.Info, "Redémarrage du launcher…");
            _ = _shell.Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown());
        }
        catch (OperationCanceledException) { LauncherUpdating = false; }
        catch (Exception ex)
        {
            var error = Errors.From(ex);
            Log.Warn($"mise à jour du launcher : {error.Code}", ex);
            LauncherUpdating = false;
            Raise("launcherupdate");
            // Automatique : pas de fenêtre bloquante pour un échec réseau, une simple notification avec « Réessayer ».
            if (automatic) _shell.Toast(UI.ToastKind.Warning, "Mise à jour du launcher impossible", error.Message, error.Code, () => _ = UpdateLauncherAsync(), "Réessayer");
            else _shell.ShowError(error, () => _ = UpdateLauncherAsync());
        }
    }

    // ================================================================== console F8 (staff)

    private void SetConsole(StaffConsoleStatus status)
    {
        StaffConsoleStatus = status;
        Raise("staffconsole");
    }

    /// <summary>Staff uniquement : compare la console installée à celle de l'API (SHA-256) et propose l'installation
    /// ou la mise à jour. Silencieux pour un joueur et quand l'API n'a pas de console (403/404/503).</summary>
    public async Task RefreshStaffConsoleAsync()
    {
        var session = Session;
        if (Preview || session is null || !session.Account.IsStaff)
        {
            if (!Preview) SetConsole(StaffConsoleStatus.Hidden);
            return;
        }

        SetConsole(StaffConsoleStatus.Checking);
        try
        {
            var info = await Api.GetStaffConsoleInfoAsync(session.AccessToken, _lifetime.Token);
            if (!ReferenceEquals(Session, session)) return;
            _consoleInfo = info;
            var installed = StaffConsole.InstalledSha();
            if (!StaffConsole.ShouldInstall(installed, info))
            {
                SetConsole(StaffConsoleStatus.UpToDate);
                return;
            }

            SetConsole(installed is null ? StaffConsoleStatus.NeedsInstall : StaffConsoleStatus.NeedsUpdate);
            // Automatique (86 Ko, SHA-256 vérifié) : jamais pendant une partie, la console y est utilisée.
            if (Op is not (Operation.Launching or Operation.Playing))
                await InstallStaffConsoleAsync(automatic: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Info($"console F8 staff indisponible : {Errors.From(ex).Code}");
            if (ReferenceEquals(Session, session)) SetConsole(StaffConsoleStatus.Unavailable);
        }
    }

    public async Task InstallStaffConsoleAsync(bool automatic = false)
    {
        var session = Session;
        if (session is null || !session.Account.IsStaff || StaffConsoleStatus == StaffConsoleStatus.Installing) return;
        if (Op is Operation.Launching or Operation.Playing)
        {
            _shell.Toast(UI.ToastKind.Warning, "Fermez le jeu d'abord",
                "La console est utilisée pendant la partie. Installez-la quand le jeu est fermé.");
            return;
        }

        var before = StaffConsoleStatus;
        SetConsole(StaffConsoleStatus.Installing);
        try
        {
            if (Preview)
            {
                await Task.Delay(900, _lifetime.Token);
            }
            else
            {
                var info = _consoleInfo ?? await Api.GetStaffConsoleInfoAsync(session.AccessToken, _lifetime.Token);
                _consoleInfo = info;
                await Task.Run(() => StaffConsole.InstallAsync(Api, session.AccessToken, info, _lifetime.Token));
            }

            SetConsole(StaffConsoleStatus.UpToDate);
            _shell.Toast(UI.ToastKind.Success, "Console F8 installée", "Elle s'ouvrira au prochain lancement du jeu.");
        }
        catch (OperationCanceledException) { SetConsole(before); }
        catch (Exception ex)
        {
            var error = Errors.From(ex);
            Log.Warn($"installation de la console F8 : {error.Code}", ex);
            SetConsole(StaffConsole.InstalledSha() is null ? StaffConsoleStatus.NeedsInstall : StaffConsoleStatus.NeedsUpdate);
            if (automatic) _shell.Toast(UI.ToastKind.Warning, "Installation de la console F8 impossible", error.Message, error.Code, () => _ = InstallStaffConsoleAsync(), "Réessayer");
            else _shell.ShowError(error, () => _ = InstallStaffConsoleAsync());
        }
    }

    /// <summary>Aperçu et tests : passe la session en modérateur avec la console dans l'état voulu.</summary>
    public void PreviewSetStaff(StaffConsoleStatus status)
    {
        Session = new Session("preview", new Account("Ranger_Staff", "moderator", DateTimeOffset.UtcNow.AddDays(30)));
        Raise("session");
        SetConsole(status);
    }

    // ================================================================== données

    public Task RefreshAllAsync()
        => Task.WhenAll(LoadProfileAsync(), LoadNotesAsync(), LoadBoardAsync(), LoadClientStateAsync());

    public async Task LoadProfileAsync()
    {
        var session = Session;
        if (session is null || Preview) return;
        Profile.Status = LoadStatus.Loading;
        Raise("profile");
        try
        {
            var profile = await Api.GetProfileAsync(session.AccessToken, _lifetime.Token);
            if (!ReferenceEquals(Session, session)) return;
            Profile.Value = profile ?? new Profile(session.Account.Username, session.Account.Role, "", RankingSummary.Empty, RankThresholds.Default);
            Profile.Error = null;
            Profile.Status = LoadStatus.Ready;
            ProfileIsApproximate = profile is null;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Fail(Profile, ex, "profil"); }
        Raise("profile");
    }

    /// <summary>Vrai si l'API ne fournit pas encore le profil détaillé (affichage « — »).</summary>
    public bool ProfileIsApproximate { get; private set; }

    public async Task LoadNotesAsync()
    {
        if (Preview) return;
        Notes.Status = LoadStatus.Loading;
        Raise("notes");
        try
        {
            Notes.Value = await Api.GetPatchNotesAsync(8, _lifetime.Token);
            Notes.Error = null;
            Notes.Status = LoadStatus.Ready;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Fail(Notes, ex, "notes de patch"); }
        Raise("notes");
    }

    public async Task LoadBoardAsync()
    {
        if (Preview) return;
        Board.Status = LoadStatus.Loading;
        Raise("board");
        try
        {
            Board.Value = await Api.GetLeaderboardAsync(100, _lifetime.Token);
            Board.Error = null;
            Board.Status = LoadStatus.Ready;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Fail(Board, ex, "classement"); }
        Raise("board");
    }

    private static void Fail<T>(Remote<T> remote, Exception ex, string what)
    {
        remote.Error = Errors.From(ex);
        remote.Status = LoadStatus.Failed;
        Log.Warn($"chargement de {what} : {remote.Error.Code}", ex);
    }

    public async Task LoadClientStateAsync()
    {
        if (Preview) return;
        ClientState = ClientState.Checking;
        Raise("client");
        try
        {
            var manifest = await Api.GetManifestAsync(_lifetime.Token)
                ?? throw new LauncherException(Errors.Manifest("manifeste absent (404)"));
            ManifestRules.Validate(manifest);
            Manifest = manifest;
            ApplyClientState();
            CheckLauncherUpdate(manifest);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            var error = Errors.From(ex);
            Log.Warn($"état du client : {error.Code}", ex);
            ClientState = ClientState.Unknown;
            Raise("client");
            _shell.Toast(UI.ToastKind.Warning, "Versions indisponibles", error.Message, error.Code,
                () => _ = LoadClientStateAsync(), "Réessayer");
        }
    }

    private void ApplyClientState()
    {
        var release = Manifest!.Client;
        InstalledVersion = ClientInstaller.InstalledVersion();
        ClientState = ClientInstaller.IsInstalled(release) ? ClientState.Ready
            : ClientInstaller.NeedsUpdate(release) ? ClientState.UpdateAvailable
            : ClientState.NotInstalled;
        Log.Info($"client : {ClientState} (installé {InstalledVersion ?? "aucun"}, publié {release.Version}) dir={ClientInstaller.ClientDir}");
        Raise("client");
    }

    private async Task ServerLoopAsync()
    {
        var ct = _lifetime.Token;
        while (!ct.IsCancellationRequested)
        {
            if (Preview)
            {
                PingMs = 24 + Random.Shared.Next(0, 9);
                ServerOnline = true;
                GameStatus=new GameServiceStatus(true,true,false,17,DateTimeOffset.UtcNow);
            }
            else
            {
                PingMs = await Api.PingAsync(ct);
                GameStatus=null;
                try { if(PingMs is not null) GameStatus=await Api.GetGameStatusAsync(ct); }
                catch(OperationCanceledException) when(ct.IsCancellationRequested) { return; }
                catch(Exception ex) { Log.Warn("statut du jeu indisponible",ex); }
                ServerOnline=PingMs is null ? false : GameStatus?.CanPlay;
            }
            if (ct.IsCancellationRequested) return;
            Raise("server");
            try { await Task.Delay(TimeSpan.FromSeconds(ServerOnline == true ? 20 : 8), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ================================================================== opérations longues

    public bool IsBusy => Op != Operation.None;

    public void CancelOperation()
    {
        if(Op==Operation.Uninstalling && Transfer?.Phase==TransferPhase.Uninstalling) return;
        _operation?.Cancel();
    }

    public bool CanUninstall => Preview ? ClientState is ClientState.Ready or ClientState.UpdateAvailable : ClientUninstaller.IsAvailable(ClientInstaller.ClientDir);

    public Task UninstallClientAsync() => RunAsync(Operation.Uninstalling,async ct =>
    {
        var directory=ClientInstaller.ClientDir;
        Transfer=new TransferProgress(TransferPhase.Preparing,"Préparation de la désinstallation…"); Raise("transfer");
        if(!Preview && GameFiles.IsGameRunning()) throw new LauncherException(Errors.AlreadyRunning());
        UninstallPlan? plan=null;
        if(!Preview)
        {
            string[]? catalogue=null;
            if(!ClientUninstaller.HasInventory(directory))
            {
                var release=Manifest?.Client ?? (await Api.GetManifestAsync(ct))?.Client;
                if(release is null) throw new LauncherException(Errors.Manifest("catalogue absent"));
                catalogue=await _installer.GetUninstallCatalogueAsync(release,ct);
            }
            plan=await Task.Run(()=>ClientUninstaller.Prepare(directory,catalogue),ct);
        }
        var approved=await _shell.Confirm("Désinstaller le client ?",
            "Les fichiers enregistrés du jeu seront supprimés. Vos réglages, touches, logs et fichiers personnels inconnus seront conservés. Le launcher et votre compte ne seront pas supprimés.",
            "Désinstaller le client",detail:plan is null ? directory : $"{plan.Directory}\n{plan.Files.Length} fichiers enregistrés · {Fmt.Size(plan.Bytes)}",icon:UI.Icons.Warning);
        ct.ThrowIfCancellationRequested();
        if(!approved) throw new UserDeclinedException();
        Transfer=new TransferProgress(TransferPhase.Uninstalling,"Suppression des fichiers du client…"); Raise("transfer");
        if(Preview)
        {
            await Task.Delay(500);
            ClientState=ClientState.NotInstalled; InstalledVersion=null; Raise("client");
            _shell.Toast(UI.ToastKind.Success,"Client désinstallé","Mode aperçu : aucune donnée réelle n'a été modifiée."); return;
        }
        var progress=new Progress<TransferProgress>(p=>{ Transfer=p; Raise("transfer"); });
        try
        {
            var result=await Task.Run(()=>ClientUninstaller.Execute(plan!,GameFiles.IsGameRunning,progress));
            _shell.Toast(UI.ToastKind.Success,"Client désinstallé",$"{result.FilesRemoved} fichiers supprimés · {Fmt.Size(result.BytesRemoved)} libérés. Vos données conservées restent dans le dossier.");
        }
        finally
        {
            _installer.TransferControl.Resume();
            if(Manifest is not null) ApplyClientState();
            else { InstalledVersion=ClientInstaller.InstalledVersion(); ClientState=File.Exists(GameFiles.ExePath)?ClientState.Unknown:ClientState.NotInstalled; Raise("client"); }
        }
    },()=>_ = UninstallClientAsync());

    private async Task RunAsync(Operation operation, Func<CancellationToken, Task> body, Action? retry)
    {
        if (IsBusy) return;
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        Op = operation;
        Transfer = null;
        Play = null;
        Raise("operation");
        try
        {
            await body(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Info($"opération {operation} annulée");
            _shell.Toast(UI.ToastKind.Info, "Opération annulée", operation is Operation.Installing or Operation.Repairing
                ? "Le téléchargement est conservé : il reprendra où il s'est arrêté." : null);
        }
        catch (UserDeclinedException)
        {
            Log.Info($"opération {operation} refusée par l'utilisateur");
        }
        catch (Exception ex)
        {
            var error = Errors.From(ex);
            Log.Error($"opération {operation} : {error.Code}", ex);
            if (error.Code == "E-AUTH-003") ExpireSession();
            else _shell.ShowError(error, retry);
        }
        finally
        {
            _operation?.Dispose();
            _operation = null;
            Op = Operation.None;
            Transfer = null;
            Raise("operation");
        }
    }

    public Task InstallOrUpdateAsync() => RunAsync(Operation.Installing, async ct =>
    {
        if (Preview) { await SimulateTransferAsync(ct, installing: true); return; }
        if (GameFiles.IsGameRunning()) throw new LauncherException(Errors.AlreadyRunning());
        var release = Manifest?.Client ?? throw new LauncherException(Errors.Manifest("manifeste non chargé"));
        var wasUpdate = ClientState == ClientState.UpdateAvailable;
        var previous = InstalledVersion;
        var progress = new Progress<TransferProgress>(p => { Transfer = p; Raise("transfer"); });
        var result = await Task.Run(() => _installer.EnsureAsync(release, progress, ct), ct);
        ApplyClientState();
        _shell.Toast(UI.ToastKind.Success, wasUpdate ? "Client mis à jour" : "Client installé", Describe(result, previous, release.Version));
    }, () => _ = InstallOrUpdateAsync());

    public Task VerifyAsync(bool repair) => RunAsync(repair ? Operation.Repairing : Operation.Verifying, async ct =>
    {
        if (Preview) { await SimulateTransferAsync(ct, installing: false); return; }
        if (GameFiles.IsGameRunning()) throw new LauncherException(Errors.AlreadyRunning());
        var release = Manifest?.Client ?? throw new LauncherException(Errors.Manifest("manifeste non chargé"));
        var progress = new Progress<TransferProgress>(p => { Transfer = p; Raise("transfer"); });
        var result = await Task.Run(() => _installer.VerifyAsync(release, repair, progress, ct), ct);
        ApplyClientState();
        _shell.Toast(UI.ToastKind.Success, repair ? "Réparation terminée" : "Fichiers vérifiés",
            result.FilesDownloaded == 0
                ? $"{result.FilesChecked} fichiers contrôlés — tout est intact."
                : $"{result.FilesDownloaded} fichier(s) corrigé(s), {Fmt.Size(result.BytesDownloaded)} retéléchargés.");
    }, () => _ = VerifyAsync(repair));

    public Task PlayAsync() => RunAsync(Operation.Launching, async ct =>
    {
        if (Preview) { await SimulatePlayAsync(ct); return; }
        var session = Session ?? throw new LauncherException(Errors.SessionExpired());
        if (ClientState != ClientState.Ready) throw new LauncherException(Errors.NotInstalled());

        var minimized = false;
        var status = new Progress<PlayStatus>(s =>
        {
            Play = s;
            if (s.Phase == PlayPhase.InGame && Op != Operation.Playing)
            {
                Op = Operation.Playing;
                Raise("operation");
            }
            if (!minimized && (s.Phase is PlayPhase.TitleScreen or PlayPhase.Loading) && Settings.MinimizeOnPlay)
            {
                minimized = true;
                _shell.SetWindowMinimized(true);
            }
            Raise("play");
        });

        var error = await Task.Run(() => new PlaySession(Api, session, s => ((IProgress<PlayStatus>)status).Report(s)).RunAsync(ct), ct);
        if (minimized) _shell.SetWindowMinimized(false);
        if (error is not null)
        {
            if (error.Code == "E-AUTH-003") { ExpireSession(); return; }
            _shell.ShowError(error, error.Retryable ? () => _ = PlayAsync() : null);
        }
        else
        {
            _shell.Toast(UI.ToastKind.Info, "Partie terminée", "Content de vous revoir. Prêt pour la suivante ?");
            _ = LoadProfileAsync(); // le score a peut-être changé
        }
    }, () => _ = PlayAsync());

    private static string Describe(ClientSyncResult result, string? previous, string version)
    {
        if (result.FullArchive) return result.WasUpdate ? $"Version {version} installée (archive complète)." : $"Version {version} prête à jouer.";
        if (!result.WasUpdate) return "Le client était déjà à jour.";
        var from = string.IsNullOrWhiteSpace(previous) ? "" : $"{previous} → ";
        return result.FilesDownloaded == 0
            ? $"{result.FilesChecked} fichiers vérifiés ({from}{version})."
            : $"{from}{version} : {result.FilesDownloaded} fichier(s), {Fmt.Size(result.BytesDownloaded)}.";
    }

    // ================================================================== maintenance / support

    public Task<ReportReceipt> SendReportAsync(ReportSubmission report, CancellationToken ct)
    {
        if (!report.Consent) throw new LauncherException(new LauncherError("E-SUPPORT-001", "Consentement requis", "Confirmez l'envoi de l'extrait affiché."));
        if (Preview) return Task.FromResult(new ReportReceipt("APERÇU — AUCUN ENVOI", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)));
        return Api.SubmitReportAsync(Session?.AccessToken ?? throw new LauncherException(Errors.SessionExpired()), report, ct);
    }

    public string? ExportDiagnostic()
    {
        try
        {
            var info = string.Join(Environment.NewLine, new[]
            {
                $"COTK Launcher {Config.Version}",
                $"Date : {DateTimeOffset.Now:O}",
                $"Système : {RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}",
                $".NET : {RuntimeInformation.FrameworkDescription}",
                $"API : {Config.ApiUrl}   Serveur de jeu : {Config.GameServer}",
                $"Compte : {Session?.Account.Username ?? "(déconnecté)"} ({Session?.Account.Role})",
                $"Client : état={ClientState} installé={InstalledVersion ?? "aucun"} publié={LatestVersion ?? "?"}",
                $"Dossier client : {ClientInstaller.ClientDir}",
                $"Latence API : {(PingMs is { } p ? p + " ms" : "hors ligne")}",
            });
            var path = Log.ExportDiagnostics(GameFiles.LogsDir, info);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("export du diagnostic impossible", ex);
            _shell.ShowError(Errors.From(ex), null);
            return null;
        }
    }

    public void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("ouverture du navigateur impossible", ex); _shell.Toast(UI.ToastKind.Warning, "Impossible d'ouvrir le navigateur", url); }
    }

    public void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("ouverture du dossier impossible", ex); }
    }

    public async Task ChangeClientFolderAsync()
    {
        if (IsBusy) { _shell.Toast(UI.ToastKind.Warning, "Opération en cours", "Attendez la fin de l'opération avant de changer de dossier."); return; }
        if (!Preview && GameFiles.IsGameRunning()) { _shell.ShowError(Errors.AlreadyRunning(), null); return; }
        var chosen = await _shell.PickFolder("Choisissez le dossier qui contient (ou contiendra) le client du jeu.");
        if (chosen is null) return;
        Settings.ClientDirectory = chosen;
        _shell.Toast(UI.ToastKind.Success, "Dossier enregistré", chosen);
        if (Manifest is not null) ApplyClientState();
    }

    // ================================================================== aperçu (--preview)

    private void SeedPreview()
    {
        var now = DateTimeOffset.UtcNow;
        Profile.Value = new Profile("IronWolf_92", "user", "preseason-3-hybrid-1",
            new RankingSummary(318_400, 1, "Bronze", 17, now.AddHours(-5), new List<RankMatch>
            {
                new(now.AddHours(-5), 41_200, 3, 7), new(now.AddHours(-30), 12_800, 18, 2), new(now.AddDays(-2), 58_300, 1, 11),
                new(now.AddDays(-3), 9_400, 24, 1), new(now.AddDays(-4), 33_900, 5, 6), new(now.AddDays(-5), 26_100, 9, 4),
                new(now.AddDays(-6), 47_800, 2, 9), new(now.AddDays(-8), 15_200, 14, 3),
            }),
            RankThresholds.Default);
        Profile.Status = LoadStatus.Ready;

        Notes.Value = new List<PatchNote>
        {
            new("Patch Alpha V0.1 — Classement et progression",
                "🏆 Classement et progression\nScore de match : chaque partie classée rapporte un score.\nTop 10 persistant : vos meilleurs scores sont conservés par compte.\nBadge de rang dans le killfeed.\n\n🧥 Apparence et équipement\nLes équipements ramassés s'affichent correctement sur le personnage.\nRetirer ou jeter un vêtement met l'apparence à jour immédiatement.\n\n⚔️ Combat\nLes morts par saignement sont créditées au dernier attaquant.\nLes cibles d'entraînement n'augmentent plus le compteur d'éliminations.",
                now.AddHours(-20)),
            new("Serveurs de nouveau en ligne", "La maintenance prolongée est terminée sur toutes les régions.\nMerci pour votre patience !", now.AddDays(-2)),
            new("Saison 3 : le classement démarre", "Nouveau palier de récompenses et rotation de carte mise à jour.\nBonne chance à tous les survivants.", now.AddDays(-4)),
            new("Patch Alpha V0 : mise à jour du gameplay", "Changement d'arme plus propre, +50 % de butin au sol, visages et coiffures respectés à la création.", now.AddDays(-9)),
        };
        Notes.Status = LoadStatus.Ready;

        var names = new[] { "fuld", "kotk", "NightOwl", "Saltwater", "Ghillie", "CrownFall", "Vandal", "RedLine", "Ashen", "Nomad77", "Halcyon", "Brigand", "Mothra", "Quiet", "Tarkov_Fan", "Zeppelin", "Cinder", "Marrow", "IronWolf_92", "Dusk" };
        var entries = new List<LeaderEntry>();
        for (var i = 0; i < names.Length; i++)
        {
            long score = names[i] == "IronWolf_92" ? 318_400 : 1_900_000 - i * 85_000;
            var tier = score >= 2_065_000 ? 6 : score >= 1_500_000 ? 5 : score >= 1_125_000 ? 4 : score >= 700_000 ? 3 : score >= 450_000 ? 2 : 1;
            entries.Add(new LeaderEntry(i + 1, "id" + i, names[i], names[i], score, tier, Tiers.Names[tier], 30 - i, now.AddHours(-i * 7)));
        }
        Board.Value = new Leaderboard(entries, "preseason-3-hybrid-1");
        Board.Status = LoadStatus.Ready;

        Manifest = new UpdateManifest(1,
            new LauncherRelease("1.0.0", "", new string('0', 64), 0),
            new ClientRelease("0.23.4.161181", "https://dl.cotk.fr/x.zip", new string('0', 64), 15_014_215_263));
        ClientState = ClientState.Ready;
        InstalledVersion = "0.23.4.161181";
    }

    /// <summary>État de l'aperçu à forcer (captures d'écran) : ready, install, update.</summary>
    public void PreviewSetClient(ClientState state)
    {
        ClientState = state;
        InstalledVersion = state == ClientState.NotInstalled ? null : state == ClientState.UpdateAvailable ? "0.23.4.161178" : "0.23.4.161181";
        Raise("client");
    }

    private async Task SimulateTransferAsync(CancellationToken ct, bool installing)
    {
        var total = 15_014_215_263L;
        var sw = Stopwatch.StartNew();
        var phases = installing
            ? new[] { (TransferPhase.Downloading, "Téléchargement du client…", 0.82), (TransferPhase.Verifying, "Vérification de l'intégrité…", 0.10), (TransferPhase.Extracting, "Extraction des fichiers…", 0.08) }
            : new[] { (TransferPhase.Analyzing, "Analyse des fichiers…", 1.0) };
        foreach (var (phase, text, share) in phases)
        {
            var phaseStart = sw.Elapsed;
            var length = TimeSpan.FromSeconds(share * (installing ? 14 : 6));
            while (sw.Elapsed - phaseStart < length)
            {
                ct.ThrowIfCancellationRequested();
                var paused=Stopwatch.StartNew();
                await _installer.TransferControl.WaitAsync(ct);
                if(paused.ElapsedMilliseconds>100)phaseStart+=paused.Elapsed;
                var f = (sw.Elapsed - phaseStart) / length;
                var rate = phase == TransferPhase.Downloading ? 62_000_000 + 9_000_000 * Math.Sin(sw.Elapsed.TotalSeconds * 1.3) : 0;
                Transfer = new TransferProgress(phase, text, (long)(total * f), total, rate, rate > 0 ? TimeSpan.FromSeconds((total - total * f) / rate) : null, (int)(3688 * f), 3688);
                Raise("transfer");
                await Task.Delay(120, ct);
            }
        }
        Transfer = new TransferProgress(TransferPhase.Done, "Terminé", total, total);
        Raise("transfer");
        await Task.Delay(300, ct);
        ClientState = ClientState.Ready;
        InstalledVersion = "0.23.4.161181";
        Raise("client");
        _shell.Toast(UI.ToastKind.Success, installing ? "Client installé" : "Fichiers vérifiés", "Mode aperçu : aucune donnée réelle n'a été modifiée.");
    }

    private async Task SimulatePlayAsync(CancellationToken ct)
    {
        var script = new (PlayPhase, string, int)[]
        {
            (PlayPhase.CheckingServer, "Vérification du serveur…", 900),
            (PlayPhase.PreparingTicket, "Préparation de votre session…", 900),
            (PlayPhase.Starting, "Démarrage du client…", 1400),
            (PlayPhase.TitleScreen, "Client prêt — choisissez « Enter Game ».", 1500),
            (PlayPhase.Loading, "Chargement du monde…", 2200),
            (PlayPhase.InGame, "En jeu. Bon combat !", 5000),
        };
        foreach (var (phase, text, ms) in script)
        {
            ct.ThrowIfCancellationRequested();
            Play = new PlayStatus(phase, text, 1, PlaySession.MaxAttempts);
            if (phase == PlayPhase.InGame) { Op = Operation.Playing; Raise("operation"); }
            Raise("play");
            await Task.Delay(ms, ct);
        }
        _shell.Toast(UI.ToastKind.Info, "Partie terminée", "Mode aperçu : le jeu n'a pas été lancé.");
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        Api.Dispose();
        _lifetime.Dispose();
    }
}
