using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace COTK.Launcher;

internal sealed class MainForm : Form
{
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HtCaption = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private readonly AuthApiClient _api;
    private readonly UpdateService _updates = new();
    private ClientRelease? _clientRelease;
    private bool _clientReady;
    private readonly CancellationTokenSource _closing = new();
    private readonly SemaphoreSlim _playOperation = new(1, 1);
    private readonly System.Windows.Forms.Timer _serverTimer = new() { Interval = 5000 };
    private LauncherSession? _session;
    private Image? _backdrop;
    private bool _launching;

    private Label _status = new();
    private Label _instructions = new();
    private Label _welcome = new();
    private Label _usernameCaption = new();
    private Label _passwordCaption = new();
    private TextBox _username = new();
    private TextBox _password = new();
    private GradientButton _connect = null!;
    private GradientButton _play = null!;
    private GradientButton _download = null!;
    private GradientButton _logout = null!;
    private Label _serverState = new();
    private StatusDot _serverDot = new();
    private Label _versionLabel = new();

    public MainForm(AuthApiClient api)
    {
        _api = api;
        _updates.ConfirmClientDownload = size => Task.FromResult(
            MessageBox.Show(
                $"Le client du jeu ({size / 1024d / 1024d / 1024d:F1} Go) doit être téléchargé depuis les serveurs COTK.{Environment.NewLine}Démarrer le téléchargement maintenant ?",
                "COTK", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1) == DialogResult.Yes);
        _updates.PickClientFolder = () =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Où installer le client du jeu ? (un dossier « client » sera créé à cet emplacement)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return Task.FromResult<string?>(null);
            try
            {
                Directory.CreateDirectory(dialog.SelectedPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dossier invalide : {ex.Message}", "COTK", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return Task.FromResult<string?>(null);
            }
            return Task.FromResult<string?>(dialog.SelectedPath);
        };
        BuildUi();
        _serverTimer.Tick += (_, _) => RefreshServerState();
        Shown += async (_, _) =>
        {
            RefreshServerState();
            _serverTimer.Start();
            await RestoreSessionAsync();
        };
        FormClosing += (_, _) =>
        {
            _closing.Cancel();
        };
        FormClosed += (_, _) =>
        {
            _serverTimer.Dispose();
            _playOperation.Dispose();
            _backdrop?.Dispose();
        };
    }

    private static Label TLabel(string text, Color color, int x, int y, float size, bool bold = false, bool mono = false)
        => new()
        {
            Text = text,
            Font = mono ? new Font("Consolas", size, bold ? FontStyle.Bold : FontStyle.Regular)
                        : Theme.Display(size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color,
            Location = new Point(x, y),
            AutoSize = false,
            Width = 600,
            Height = (int)(size * 2.2f),
        };

    private static Label MonoLabel(string text, Color color, int x, int y, float size = 9F)
        => new()
        {
            Text = text,
            Font = new Font("Consolas", size),
            ForeColor = color,
            Location = new Point(x, y),
            AutoSize = false,
            Width = 500,
            Height = (int)(size * 2.4f),
        };

    private static Panel Rule(int x, int y, int width, Color? color = null) => new()
    {
        Location = new Point(x, y),
        Size = new Size(width, 2),
        BackColor = color ?? Theme.Warn,
    };

    private static Button WindowButton(string text, int x)
    {
        var button = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 10F),
            ForeColor = Theme.InkDim,
            BackColor = Theme.Deep,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(x, 0),
            Size = new Size(40, 58),
            TabStop = false,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 52, 61);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(91, 18, 22);
        return button;
    }

    private static TextBox LoginInput(string placeholder, int y, bool password = false) => new()
    {
        Location = new Point(24, y),
        Size = new Size(286, 35),
        AutoSize = false,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(12, 14, 18),
        ForeColor = Theme.Ink,
        Font = new Font("Segoe UI", 10F),
        PlaceholderText = placeholder,
        UseSystemPasswordChar = password,
    };

    private void BuildUi()
    {
        DoubleBuffered = true;
        Text = "COTK - Crown of the King";
        ClientSize = new Size(1120, 700);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.BgMid;
        _backdrop = LoadClientBackdrop();
        Paint += DrawBackground;

        var header = new Panel { Location = new Point(0, 0), Size = new Size(1120, 58), BackColor = Theme.Deep };
        var brandMark = new Panel { Location = new Point(20, 14), Size = new Size(4, 30), BackColor = Theme.Red500 };
        var brand = TLabel("COTK", Color.White, 34, 9, 21F, bold: true);
        brand.Width = 100;
        var tabPlay = MonoLabel("JOUER", Color.White, 158, 19, 9F);
        var tabPlayBar = new Panel { Location = new Point(153, 48), Size = new Size(62, 2), BackColor = Theme.Warn };
        var tabNews = MonoLabel("ACTUALITÉS", Theme.InkMute, 246, 19, 9F);
        var tabRanks = MonoLabel("CLASSEMENT", Theme.InkMute, 358, 19, 9F);

        _serverDot = new StatusDot { Location = new Point(832, 21) };
        var serverCaption = MonoLabel("SERVEUR", Theme.InkMute, 852, 12, 7.5F);
        serverCaption.Width = 120;
        _serverState = MonoLabel("VÉRIFICATION...", Theme.InkDim, 852, 27, 8F);
        _serverState.Width = 170;

        var minimize = WindowButton("—", 1040);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var close = WindowButton("X", 1080);
        close.ForeColor = Color.White;
        close.Click += (_, _) => Close();

        header.Controls.AddRange(new Control[]
        {
            brandMark, brand, tabPlay, tabPlayBar, tabNews, tabRanks,
            _serverDot, serverCaption, _serverState, minimize, close,
        });
        header.MouseDown += BeginWindowDrag;
        foreach (Control control in header.Controls)
        {
            if (control is not Button)
                control.MouseDown += BeginWindowDrag;
        }
        Controls.Add(header);

        var eyebrow = MonoLabel("PRE-SEASON 3  /  BUILD 0.23.4", Theme.Red400, 38, 93, 9F);
        eyebrow.Width = 500;
        var heroTitle = TLabel("CROWN OF THE KING", Color.White, 36, 112, 31F, bold: true);
        heroTitle.Width = 690;
        heroTitle.Height = 68;
        var heroSubtitle = TLabel("BATTLE ROYALE RESTAURÉ", Theme.InkDim, 39, 169, 13F);
        heroSubtitle.Width = 500;
        var heroRule = Rule(39, 207, 84, Theme.Warn);
        var heroMeta = MonoLabel("SOLO  •  CARTE Z2  •  DERNIER SURVIVANT", Theme.Ink, 39, 219, 9F);
        heroMeta.Width = 600;
        Controls.AddRange(new Control[] { eyebrow, heroTitle, heroSubtitle, heroRule, heroMeta });

        var newsCard = new ClientPanel
        {
            Location = new Point(34, 270),
            Size = new Size(680, 352),
            AccentColor = Theme.Red500,
        };
        var newsCaption = MonoLabel("ACTUALITÉS DU FRONT", Theme.Red400, 24, 19, 8.5F);
        newsCaption.Width = 300;
        var newsTitle = TLabel("NOTES DE PATCH", Color.White, 23, 35, 17F, bold: true);
        newsTitle.Width = 300;
        var newsRule = Rule(24, 68, 632, Color.FromArgb(100, Theme.Border));
        var news = new RichTextBox
        {
            Location = new Point(24, 80),
            Size = new Size(632, 248),
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Panel,
            ForeColor = Theme.InkDim,
            Font = new Font("Segoe UI", 9.5F),
            ReadOnly = true,
            DetectUrls = false,
            TabStop = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        FillNews(news);
        newsCard.Controls.AddRange(new Control[] { newsCaption, newsTitle, newsRule, news });
        Controls.Add(newsCard);

        var loginCard = new ClientPanel
        {
            Location = new Point(752, 88),
            Size = new Size(334, 534),
            AccentColor = Theme.Warn,
        };
        var cardCaption = MonoLabel("ACCÈS AU COMBAT", Theme.Warn, 24, 20, 8.5F);
        cardCaption.Width = 260;
        var cardTitle = TLabel("BATTLE ROYALE", Color.White, 23, 37, 19F, bold: true);
        cardTitle.Width = 286;

        var mode = new Panel { Location = new Point(24, 79), Size = new Size(286, 64), BackColor = Color.FromArgb(28, 31, 37) };
        var modeAccent = new Panel { Location = new Point(0, 0), Size = new Size(3, 64), BackColor = Theme.Red500 };
        var modeName = TLabel("SOLO", Color.White, 16, 9, 13F, bold: true);
        modeName.Width = 120;
        var modeMeta = MonoLabel("Z2  /  150 JOUEURS MAX", Theme.InkMute, 16, 36, 8F);
        modeMeta.Width = 230;
        mode.Controls.AddRange(new Control[] { modeAccent, modeName, modeMeta });

        _instructions = MonoLabel("IDENTIFIEZ-VOUS POUR REJOINDRE LE COMBAT.\nVotre session restera protégée sur cet ordinateur.", Theme.InkDim, 24, 162, 8.5F);
        _instructions.Width = 286;
        _instructions.Height = 46;

        _usernameCaption = MonoLabel("PSEUDO OU E-MAIL", Theme.InkMute, 24, 218, 8F);
        _usernameCaption.Width = 286;
        _username = LoginInput("Votre identifiant", 239);

        _passwordCaption = MonoLabel("MOT DE PASSE", Theme.InkMute, 24, 286, 8F);
        _passwordCaption.Width = 286;
        _password = LoginInput("Votre mot de passe", 307, password: true);
        _password.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await LoginAsync();
        };

        _connect = new GradientButton(
            "Se connecter",
            Theme.Red400, Theme.Red500, Theme.Red700, Theme.Red300)
        { Location = new Point(24, 356), Size = new Size(286, 52) };
        _connect.Click += async (_, _) => await LoginAsync();

        _status = MonoLabel("Utilisez le compte créé sur le site COTK.", Theme.InkDim, 24, 426, 8.5F);
        _status.Width = 286;
        _status.Height = 38;

        _welcome = TLabel("", Theme.Warn, 24, 169, 13F, bold: true);
        _welcome.Width = 286;
        _welcome.Height = 48;
        _welcome.Visible = false;

        _play = new GradientButton(
            "JOUER MAINTENANT",
            Theme.Red400, Theme.Red500, Theme.Red700, Theme.Red300)
        { Location = new Point(24, 232), Size = new Size(286, 58), Visible = false };
        _play.Font = Theme.Display(15F, FontStyle.Bold);
        _play.Click += async (_, _) => await PlayAsync();

        _download = new GradientButton(
            "TÉLÉCHARGER LE CLIENT",
            Theme.Warn, Theme.Amber, Theme.GoldBorder, Theme.Warn,
            Color.FromArgb(255, 216, 64), Theme.Amber)
        { Location = new Point(24, 232), Size = new Size(286, 58), Visible = false };
        _download.Font = Theme.Display(12F, FontStyle.Bold);
        _download.Click += async (_, _) => await DownloadClientAsync();

        _logout = new GradientButton(
            "Se déconnecter",
            Theme.Steel, Theme.SteelDark, Color.FromArgb(30, 37, 46), Theme.GoldBorder,
            Color.FromArgb(158, 113, 26), Color.FromArgb(86, 52, 11))
        { Location = new Point(24, 304), Size = new Size(286, 42), Visible = false };
        _logout.Click += async (_, _) => await LogoutAsync();

        var cardRule = Rule(24, 466, 286, Color.FromArgb(90, Theme.Border));
        var safety = MonoLabel("SESSION PROTÉGÉE  •  TICKET SIGNÉ TEMPORAIRE", Theme.InkMute, 24, 481, 7.5F);
        safety.Width = 286;

        loginCard.Controls.AddRange(new Control[]
        {
            cardCaption, cardTitle, mode, _instructions, _usernameCaption, _username,
            _passwordCaption, _password, _connect,
            _status, _welcome, _play, _download, _logout, cardRule, safety,
        });
        Controls.Add(loginCard);

        var footerLine = Rule(34, 650, 1052, Color.FromArgb(55, Theme.Border));
        var footerLeft = MonoLabel("COTK  /  WINDOWS 10-11  /  COMPTE REQUIS", Theme.InkMute, 34, 665, 7.5F);
        footerLeft.Width = 500;
        _versionLabel = MonoLabel(
            $"CLIENT PS3  •  BUILD 0.23.4.161178  •  LAUNCHER {UpdateService.CurrentLauncherVersion()}",
            Theme.InkMute,
            720,
            665,
            7.5F);
        _versionLabel.Width = 366;
        _versionLabel.TextAlign = ContentAlignment.TopRight;
        Controls.AddRange(new Control[] { footerLine, footerLeft, _versionLabel });
    }

    private async Task RestoreSessionAsync()
    {
        _connect.Enabled = false;
        _username.Enabled = false;
        _password.Enabled = false;
        SetStatus("Vérification de la session...", Theme.InkDim);
        string? token;
        try
        {
            token = CredentialStore.ReadToken();
        }
        catch
        {
            ShowSignedOut("Impossible de lire la session Windows.");
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            ShowSignedOut();
            return;
        }

        try
        {
            var account = await _api.GetCurrentAccountAsync(token, _closing.Token);
            if (_closing.IsCancellationRequested) return;
            if (account.IsAdmin)
                TryDeleteCredential();
            ShowSignedIn(new LauncherSession(token, account));
        }
        catch (AuthApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            TryDeleteCredential();
            ShowSignedOut("Votre session a expiré. Reconnectez-vous.");
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch
        {
            ShowSignedOut("Service COTK indisponible. Réessayez plus tard.");
        }
    }

    /// <summary>Apres login (manuel ou session restauree) : mise a jour du
    /// launcher si publiee, puis detection de l'etat du client. Aucune
    /// verification ni telechargement avant authentification.</summary>
    private async Task PrepareAfterLoginAsync()
    {
        SetStatus("Vérification des versions...", Theme.InkDim);
        try
        {
            var launcherUpdate = await _updates.CheckLauncherUpdateAsync(_closing.Token);
            if (launcherUpdate is not null)
            {
                SetStatus($"Mise à jour du launcher {launcherUpdate.Version}...", Theme.Warn);
                await _updates.ScheduleLauncherUpdateAsync(
                    launcherUpdate,
                    new Progress<string>(message => SetStatus(message, Theme.Warn)),
                    _closing.Token);
                BeginInvoke(Close);
                return;
            }

            _clientRelease = await _updates.GetClientReleaseAsync(_closing.Token);
            _clientReady = UpdateService.IsClientInstalled(_clientRelease);
            GameLauncher.Log(
                $"post-login: client {_clientRelease.Version} installed={_clientReady} dir={GameLauncher.ClientDir}");
            RefreshGameControls();
            _versionLabel.Text =
                $"CLIENT PS3  •  BUILD {_clientRelease.Version}  •  LAUNCHER {UpdateService.CurrentLauncherVersion()}";
            if (_clientReady)
                SetStatus("Compte vérifié. Prêt à jouer.", Theme.Ok);
            else
                SetStatus("Le client n'est pas encore installé. Cliquez sur TÉLÉCHARGER LE CLIENT.", Theme.Warn);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (UpdateException ex)
        {
            SetStatus($"Vérification impossible : {ex.Message}", Theme.Red400);
        }
        catch (Exception ex)
        {
            GameLauncher.Log($"prepare-after-login failed: {ex.GetType().Name}: {ex.Message}");
            SetStatus("Service COTK indisponible. Réessayez plus tard.", Theme.Red400);
        }
    }

    private bool _downloading;

    private async Task DownloadClientAsync()
    {
        if (_downloading || _launching) return;
        _downloading = true;
        _download.Enabled = false;
        _play.Enabled = false;
        _logout.Enabled = false;
        try
        {
            var release = _clientRelease ?? await _updates.GetClientReleaseAsync(_closing.Token);
            _clientRelease = release;
            await _updates.EnsureClientAsync(
                release,
                new Progress<string>(message => SetStatus(message, Theme.InkDim)),
                _closing.Token);
            _clientReady = UpdateService.IsClientInstalled(release);
            RefreshGameControls();
            SetStatus(_clientReady
                ? "Client installé. Prêt à jouer !"
                : "Téléchargement terminé mais installation incomplète. Consultez launcher\\data\\launcher.log.",
                _clientReady ? Theme.Ok : Theme.Red400);
            GameLauncher.Log($"client download: done, version {release.Version}");
        }
        catch (ClientDownloadDeclinedException)
        {
            GameLauncher.Log("client download declined by user");
            SetStatus("Téléchargement annulé. Utilisez TÉLÉCHARGER LE CLIENT pour le relancer.", Theme.Warn);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (UpdateException ex)
        {
            SetStatus($"Téléchargement impossible : {ex.Message}", Theme.Red400);
        }
        catch (Exception ex)
        {
            GameLauncher.Log($"client download failed: {ex.GetType().Name}: {ex.Message}");
            SetStatus("Le téléchargement a échoué. Consultez launcher\\data\\launcher.log.", Theme.Red400);
        }
        finally
        {
            _downloading = false;
            if (!IsDisposed && !_closing.IsCancellationRequested)
            {
                _download.Enabled = true;
                _logout.Enabled = _session is not null;
                RefreshGameControls();
            }
        }
    }

    private async Task LoginAsync()
    {
        var username = _username.Text.Trim();
        if (username.Length == 0 || _password.Text.Length == 0)
        {
            SetStatus("Saisissez votre identifiant et votre mot de passe.", Theme.Red400);
            return;
        }

        _connect.Enabled = false;
        _username.Enabled = false;
        _password.Enabled = false;
        _connect.Text = "CONNEXION...";
        SetStatus("Vérification de votre compte...", Theme.InkDim);

        try
        {
            var session = await _api.LoginAsync(username, _password.Text, _closing.Token);
            if (session.Account.IsAdmin)
                TryDeleteCredential();
            else
                CredentialStore.WriteToken(session.AccessToken);
            if (_closing.IsCancellationRequested) return;
            _username.Clear();
            _password.Clear();
            ShowSignedIn(session);
        }
        catch (AuthApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _password.Clear();
            SetStatus("Identifiant ou mot de passe incorrect.", Theme.Red400);
        }
        catch (AuthApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _password.Clear();
            SetStatus("Ce compte ne peut pas accéder au jeu.", Theme.Red400);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch
        {
            ShowSignedOut("Connexion impossible. Réessayez plus tard.");
        }
        finally
        {
            if (!IsDisposed && !_closing.IsCancellationRequested && _session is null)
            {
                _connect.Enabled = true;
                _connect.Text = "SE CONNECTER";
                _username.Enabled = true;
                _password.Enabled = true;
            }
        }
    }

    private async Task LogoutAsync()
    {
        var session = _session;
        if (session is null) return;
        _logout.Enabled = false;
        try
        {
            await _api.LogoutAsync(session.AccessToken, _closing.Token);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { }
        finally
        {
            TryDeleteCredential();
            _session = null;
            if (!IsDisposed && !_closing.IsCancellationRequested)
                ShowSignedOut("Session fermée sur cet ordinateur.");
        }
    }

    private async Task PlayAsync()
    {
        if (!await _playOperation.WaitAsync(0)) return;
        var session = _session;
        if (session is null)
        {
            _playOperation.Release();
            return;
        }

        _launching = true;
        _play.Enabled = false;
        _logout.Enabled = false;
        _play.Text = "DÉMARRAGE...";
        var operationId = Guid.NewGuid().ToString("N")[..8];
        GameLauncher.Log($"op={operationId} launch requested");

        try
        {
            if (!_clientReady || _clientRelease is null)
            {
                RefreshGameControls();
                SetStatus("Le client n'est pas installé. Cliquez sur TÉLÉCHARGER LE CLIENT.", Theme.Warn);
                return;
            }
            if (GameLauncher.IsGameRunning())
            {
                SetStatus("Le jeu est déjà ouvert.", Theme.Warn);
                GameLauncher.Log($"op={operationId} skipped because H1Z1 is already running");
                return;
            }

            SetStatus("Vérification du serveur...", Theme.InkDim);
            if (!GameLauncher.ServerPortsUp())
            {
                RefreshServerState();
                GameLauncher.Log($"op={operationId} server offline");
                SetStatus("Serveur hors ligne ! Relance JOUER.bat à la racine.", Theme.Red400);
                return;
            }

            RefreshServerState();
            const int maxAttempts = 10;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                _closing.Token.ThrowIfCancellationRequested();
                if (!GameLauncher.ServerPortsUp())
                    throw new InvalidOperationException("Game server became unavailable.");

                SetStatus($"Préparation du jeu - tentative {attempt}/{maxAttempts}...", Theme.InkDim);
                var ticket = await _api.CreateGameTicketAsync(session.AccessToken, _closing.Token);
                GameLauncher.WriteGameTicket(ticket);
                using var game = GameLauncher.StartGame(attempt, operationId);
                var ticketRefreshedForThisLoading = false;
                var result = await GameLauncher.MonitorGameAsync(
                    game,
                    attempt,
                    operationId,
                    stage =>
                    {
                        ShowClientStage(stage, attempt, maxAttempts);
                        // Ticket à la volée sur exit match (WaitingForReloginSession)
                        if (stage == GameLauncher.ClientStage.LoadingWorld && !ticketRefreshedForThisLoading)
                        {
                            try
                            {
                                var liveLog = Path.Combine(GameLauncher.ClientDir, "Logs", "H1Z1 PlayClient (Live).log");
                                string log = "";
                                try
                                {
                                    using var stream = new FileStream(liveLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                                    log = reader.ReadToEnd();
                                }
                                catch { }
                                if (log.Contains("WaitingForReloginSession", StringComparison.Ordinal))
                                {
                                    ticketRefreshedForThisLoading = true;
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            GameLauncher.Log($"op={operationId} exit-match detected in Monitor, refreshing ticket");
                                            var fresh = await _api.CreateGameTicketAsync(session.AccessToken, CancellationToken.None);
                                            GameLauncher.WriteGameTicket(fresh);
                                            GameLauncher.Log($"op={operationId} ticket refreshed expires={fresh.ExpiresAt:O}");
                                        }
                                        catch (Exception ex)
                                        {
                                            GameLauncher.Log($"op={operationId} ticket refresh failed: {ex.Message}");
                                        }
                                    });
                                }
                            }
                            catch { }
                        }
                        else if (stage == GameLauncher.ClientStage.InGame)
                        {
                            ticketRefreshedForThisLoading = false;
                        }
                    },
                    _closing.Token);

                if (result.Outcome == GameLauncher.AttemptOutcome.InGame)
                {
                    GameLauncher.StartToggleConsole(session.Account, game, operationId);
                    SetStatus("Connexion terminée. Bon combat !", Theme.Warn);
                    // Reste en surveillance pour distribuer un ticket frais sur exit match
                    // (WaitingForReloginSession -> GAMESTATE_LOADINGSCREEN). Sans cela un ticket
                    // de 60s/30m expire et le re-login après 1h reste bloqué.
                    await WatchPostGameForReloginAsync(game, operationId, session);
                    return;
                }

                if (!result.ShouldRetry)
                {
                    if (result.Outcome == GameLauncher.AttemptOutcome.AuthenticationRejected)
                        SetStatus("Ticket refusé par le serveur. Relancez JOUER.bat pour réparer les services.", Theme.Red400);
                    else
                        SetStatus(result.ExitCode.HasValue
                            ? "Le jeu a été fermé depuis le menu."
                            : "Le jeu est ouvert et attend votre action.", Theme.InkDim);
                    return;
                }

                if (attempt < maxAttempts)
                {
                    SetStatus($"Le client a rencontré un problème. Nouvelle tentative automatique {attempt + 1}/{maxAttempts}...", Theme.Red400);
                    await Task.Delay(TimeSpan.FromSeconds(3), _closing.Token);
                }
            }

            SetStatus("Le client a échoué après 10 tentatives. Consultez launcher\\data\\diagnostics.", Theme.Red400);
        }
        catch (AuthApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            TryDeleteCredential();
            ShowSignedOut("Votre session a expiré. Reconnectez-vous.");
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception ex)
        {
            GameLauncher.Log($"op={operationId} launch failed: {ex.GetType().Name}: {ex.Message}");
            SetStatus("Le lancement a échoué. Consultez launcher\\data\\launcher.log.", Theme.Red400);
        }
        finally
        {
            _launching = false;
            _playOperation.Release();
            if (!IsDisposed && !_closing.IsCancellationRequested)
            {
                _logout.Enabled = _session is not null;
                RefreshGameControls();
            }
        }
    }

    private void ShowClientStage(GameLauncher.ClientStage stage, int attempt, int maxAttempts)
    {
        if (IsDisposed || _closing.IsCancellationRequested) return;
        var message = stage switch
        {
            GameLauncher.ClientStage.TitleScreen => "Client prêt. Cliquez sur Enter Game.",
            GameLauncher.ClientStage.LoadingWorld => "Entrée en jeu et chargement du monde...",
            GameLauncher.ClientStage.InGame => "Monde chargé. Surveillance de la phase lobby et parachute...",
            _ => $"Démarrage du client - tentative {attempt}/{maxAttempts}...",
        };
        SetStatus(message, stage == GameLauncher.ClientStage.InGame ? Theme.Warn : Theme.InkDim);
    }

    private async Task WatchPostGameForReloginAsync(Process game, string operationId, LauncherSession session)
    {
        var liveLog = Path.Combine(GameLauncher.ClientDir, "Logs", "H1Z1 PlayClient (Live).log");
        var startedAt = DateTimeOffset.UtcNow;
        var lastStage = GameLauncher.ClientStage.InGame;
        var ticketRefreshedForThisLoading = false;
        GameLauncher.Log($"op={operationId} post-game watch started pid={game.Id}");
        while (true)
        {
            _closing.Token.ThrowIfCancellationRequested();
            game.Refresh();
            if (game.HasExited)
            {
                GameLauncher.Log($"op={operationId} post-game watch: process exited code={game.ExitCode}");
                if (!IsDisposed && !_closing.IsCancellationRequested)
                    SetStatus("Le jeu a été fermé.", Theme.InkDim);
                return;
            }
            string log = "";
            try
            {
                var info = new FileInfo(liveLog);
                if (info.Exists && info.LastWriteTimeUtc >= startedAt.UtcDateTime.AddSeconds(-5))
                {
                    using var stream = new FileStream(liveLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                    log = reader.ReadToEnd();
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(log))
            {
                var stage = GameLauncher.DetectClientStage(log);
                if (stage != lastStage)
                {
                    lastStage = stage;
                    GameLauncher.Log($"op={operationId} post-game stage={stage} logLen={log.Length}");
                    if (stage == GameLauncher.ClientStage.LoadingWorld && log.Contains("WaitingForReloginSession", StringComparison.Ordinal))
                    {
                        ticketRefreshedForThisLoading = false;
                    }
                    if (stage == GameLauncher.ClientStage.InGame)
                        ticketRefreshedForThisLoading = false;
                }
                // Exit match = LoadingWorld avec WaitingForReloginSession dedans
                if (lastStage == GameLauncher.ClientStage.LoadingWorld
                    && !ticketRefreshedForThisLoading
                    && log.Contains("WaitingForReloginSession", StringComparison.Ordinal))
                {
                    ticketRefreshedForThisLoading = true;
                    try
                    {
                        SetStatus("Retour au lobby — distribution d'un ticket frais...", Theme.Warn);
                        GameLauncher.Log($"op={operationId} exit-match detected, refreshing game ticket");
                        var ticket = await _api.CreateGameTicketAsync(session.AccessToken, _closing.Token);
                        GameLauncher.WriteGameTicket(ticket);
                        GameLauncher.Log($"op={operationId} new ticket issued expires={ticket.ExpiresAt:O}");
                        SetStatus("Ticket frais distribué. Retour au lobby en cours...", Theme.Ok);
                    }
                    catch (Exception ex)
                    {
                        GameLauncher.Log($"op={operationId} ticket refresh failed: {ex.GetType().Name}: {ex.Message}");
                        SetStatus("Ticket de relogin indisponible — le lobby peut rester bloqué.", Theme.Red400);
                    }
                }
            }
            await Task.Delay(500, _closing.Token);
        }
    }

    private void ShowSignedIn(LauncherSession session)
    {
        _session = session;
        _instructions.Visible = false;
        _usernameCaption.Visible = false;
        _username.Visible = false;
        _passwordCaption.Visible = false;
        _password.Visible = false;
        _connect.Visible = false;
        _welcome.Visible = true;
        _play.Visible = true;
        _logout.Visible = true;
        _logout.Enabled = true;
        _welcome.Text = session.Account.IsAdmin
            ? $"BIENVENUE, {session.Account.Username.ToUpperInvariant()} - ADMIN F8"
            : $"BIENVENUE, {session.Account.Username.ToUpperInvariant()}";
        _welcome.ForeColor = session.Account.IsAdmin ? Theme.Warn : Color.White;
        SetStatus("Vérification des versions...", Theme.InkDim);
        RefreshGameControls();
        _ = PrepareAfterLoginAsync();
    }

    private void ShowSignedOut(string message = "Utilisez votre compte COTK pour vous connecter.")
    {
        if (IsDisposed || _closing.IsCancellationRequested) return;
        _session = null;
        _instructions.Visible = true;
        _usernameCaption.Visible = true;
        _username.Visible = true;
        _username.Enabled = true;
        _passwordCaption.Visible = true;
        _password.Visible = true;
        _password.Enabled = true;
        _password.Clear();
        _connect.Visible = true;
        _connect.Enabled = true;
        _connect.Text = "SE CONNECTER";
        _welcome.Visible = false;
        _play.Visible = false;
        _download.Visible = false;
        _logout.Visible = false;
        _clientReady = false;
        SetStatus(message, Theme.InkDim);
    }

    private static void TryDeleteCredential()
    {
        try { CredentialStore.DeleteToken(); } catch { }
    }

    private void SetStatus(string message, Color color)
    {
        if (IsDisposed || _closing.IsCancellationRequested) return;
        _status.Text = message;
        _status.ForeColor = color;
    }

    private void RefreshServerState()
    {
        if (IsDisposed || _closing.IsCancellationRequested) return;
        try
        {
            var online = GameLauncher.ServerPortsUp();
            _serverDot.DotColor = online ? Theme.Ok : Theme.Red400;
            _serverState.Text = online ? "EN LIGNE  /  PRÊT" : "HORS LIGNE";
            _serverState.ForeColor = online ? Theme.Ok : Theme.InkDim;
        }
        catch
        {
            _serverDot.DotColor = Theme.InkMute;
            _serverState.Text = "ÉTAT INCONNU";
            _serverState.ForeColor = Theme.InkMute;
        }
        RefreshGameControls();
    }

    private void RefreshGameControls()
    {
        if (IsDisposed || _closing.IsCancellationRequested || _session is null || _launching) return;
        var running = GameLauncher.IsGameRunning();
        var ready = _clientReady && !_downloading;
        _play.Visible = ready;
        _download.Visible = !ready;
        _download.Enabled = !_downloading;
        _play.Text = running ? "JEU EN COURS" : "JOUER MAINTENANT";
        _play.Enabled = !running;
    }

    private static Image? LoadClientBackdrop()
    {
        var path = Path.Combine(GameLauncher.ClientDir, "LaunchPad.libs", "Web", "images", "background.jpg");
        if (!File.Exists(path)) return null;
        try
        {
            using var source = Image.FromFile(path);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private void DrawBackground(object? sender, PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width == 0 || rect.Height == 0) return;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var gradient = new LinearGradientBrush(rect, Theme.BgTop, Theme.BgBot, 50F))
        {
            gradient.InterpolationColors = new ColorBlend(3)
            {
                Colors = new[] { Theme.BgTop, Theme.BgMid, Theme.BgBot },
                Positions = new[] { 0f, 0.58f, 1f },
            };
            e.Graphics.FillRectangle(gradient, rect);
        }

        if (_backdrop is not null)
        {
            var scale = Math.Max((float)rect.Width / _backdrop.Width, (float)rect.Height / _backdrop.Height);
            var width = _backdrop.Width * scale;
            var height = _backdrop.Height * scale;
            var imageRect = new RectangleF((rect.Width - width) / 2F, (rect.Height - height) / 2F, width, height);
            e.Graphics.DrawImage(_backdrop, imageRect);
        }

        using (var shade = new LinearGradientBrush(rect, Color.FromArgb(205, 5, 6, 9), Color.FromArgb(115, 8, 5, 7), 0F))
            e.Graphics.FillRectangle(shade, rect);
        using (var topShade = new LinearGradientBrush(rect, Color.FromArgb(195, 3, 4, 6), Color.Transparent, 90F))
            e.Graphics.FillRectangle(topShade, 0, 58, rect.Width, 240);

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect.Right - 470, 40, 720, 610);
            using var glow = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(52, 145, 12, 18),
                SurroundColors = new[] { Color.Transparent },
            };
            e.Graphics.FillPath(glow, path);
        }

        using (var slash = new SolidBrush(Color.FromArgb(22, Theme.Red500)))
        {
            e.Graphics.FillPolygon(slash, new[]
            {
                new Point(555, 58), new Point(650, 58), new Point(470, 650), new Point(390, 650),
            });
        }
        using (var border = new Pen(Color.FromArgb(110, Theme.Border)))
            e.Graphics.DrawRectangle(border, 0, 0, rect.Width - 1, rect.Height - 1);
    }

    private sealed record PatchNote(string Title, string Body, DateTimeOffset PublishedAt);

    private sealed record PatchNotesResponse(List<PatchNote> PatchNotes);

    private static void FillNews(RichTextBox box)
    {
        FillFallbackNews(box);
        _ = FetchPatchNotesAsync(box);
    }

    /// <summary>Charge les patch notes publies via le panneau admin ; garde le
    /// contenu statique si l'API est absente ou hors ligne.</summary>
    private static async Task FetchPatchNotesAsync(RichTextBox box)
    {
        try
        {
            var apiBase = COTK.Launcher.LauncherConfig.ApiUrl.TrimEnd('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var response = await http.GetFromJsonAsync<PatchNotesResponse>(
                $"{apiBase}/api/v1/patchnotes?limit=6",
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var notes = response?.PatchNotes ?? new List<PatchNote>();
            if (notes.Count == 0) return;
            if (box.IsDisposed || !box.IsHandleCreated) return;
            box.BeginInvoke(() => RenderPatchNotes(box, notes));
        }
        catch
        {
            // API injoignable : le contenu de secours reste affiche.
        }
    }

    private static void RenderPatchNotes(RichTextBox box, List<PatchNote> notes)
    {
        void Head(string text) => AppendColored(box, $"\n  {text}\n", Theme.Warn, 11F, FontStyle.Bold);
        void Item(string text) => AppendColored(box, $"  {text}\n", Theme.InkDim, 9.5F, FontStyle.Regular);
        void Dim(string text) => AppendColored(box, $"  {text}\n\n", Theme.InkMute, 8.5F, FontStyle.Regular);

        box.Clear();
        foreach (var note in notes.Take(5))
        {
            Head(note.Title.ToUpperInvariant());
            foreach (var line in note.Body.Replace("\r\n", "\n").Split('\n'))
                Item(line);
            Dim($"  - {note.PublishedAt.ToLocalTime():dd MMMM yyyy} - EQUIPE COTK");
        }
        box.SelectionStart = 0;
        box.SelectionLength = 0;
        box.ScrollToCaret();
    }

    private static void FillFallbackNews(RichTextBox box)
    {
        void Head(string text) => AppendColored(box, $"\n  {text}\n", Theme.Warn, 11F, FontStyle.Bold);
        void Item(string text) => AppendColored(box, $"  {text}\n", Theme.InkDim, 9.5F, FontStyle.Regular);
        void Dim(string text) => AppendColored(box, $"  {text}\n\n", Theme.InkMute, 8.5F, FontStyle.Regular);

                Head("PATCH v1.2 - 24 AOUT 2026");
        Item("* LAUNCHER : authentification securisee via le site COTK.");
        Item("* SESSION : stockage protege par le Gestionnaire d'identification Windows.");
        Dim("  - EQUIPE COTK");
        Head("PATCH v1.1 - 24 AOUT 2026");
        Item("* RESEAU : refonte du flux de chargement du monde.");
        Item("* ADMIN : console F8 reservee au role administrateur du site.");
        Dim("  - EQUIPE COTK");
        Head("PATCH v1.0 - 23 AOUT 2026");
        Item("* Premier match Battle Royale solo jouable sur le build PS3 authentique.");
        Item("* LOOT, COMBAT, VEHICULES, PERSONNALISATION.");
        Dim("  - UNE COURONNE A LA FOIS.");
        
box.SelectionStart = 0;
        box.SelectionLength = 0;
        box.ScrollToCaret();
    }

    private static void AppendColored(RichTextBox box, string text, Color color, float size, FontStyle style)
    {
        var start = box.TextLength;
        box.AppendText(text);
        box.SelectionStart = start;
        box.SelectionLength = text.Length;
        box.SelectionColor = color;
        box.SelectionFont = new Font(box.Font.FontFamily, size, style);
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WmNcLeftButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    protected override void WndProc(ref Message message)
    {
        const int wmNcHitTest = 0x0084;
        const int htClient = 1;
        const int htCaption = 2;

        base.WndProc(ref message);
        if (message.Msg != wmNcHitTest || message.Result.ToInt64() != htClient) return;

        var value = message.LParam.ToInt64();
        var screenPoint = new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff));
        var clientPoint = PointToClient(screenPoint);
        if (clientPoint.Y is >= 0 and < 58 && clientPoint.X < 1030)
            message.Result = (IntPtr)htCaption;
    }
}
