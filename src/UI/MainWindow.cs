using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Fenêtre principale : barre latérale, barre du haut, pages, notifications, boîtes modales, écran
/// de connexion. Sans bordure système (WindowChrome) mais avec ombre, instantanés Windows 11, déplacement
/// et réduction natifs.</summary>
internal sealed class MainWindow : Window, IShell, INavigator
{
    private const double DesignW = 1120;
    private const double DesignH = 700;
    private const double SidebarW = 216;
    private const double TopBarH = 58;

    private readonly LauncherApp _app;
    private readonly string? _startScreen;
    private readonly Grid _frame = new();
    private readonly Grid _shell = new();
    private LoginView? _login;
    private readonly ModalHost _modal = new();
    private readonly ToastHost _toasts = new();
    private readonly Grid _viewHost = new();
    private readonly Dictionary<Page, View> _views = new();
    private readonly Dictionary<Page, NavItem> _nav = new();
    private readonly Grid _sidebar = new();
    private readonly Border _indicator = new();
    private readonly TranslateTransform _indicatorMove = new();
    private readonly TextBlock _title = new();
    private readonly Run _titleLight = new();
    private readonly Run _titleStrong = new();
    private readonly Border _serverDot = Ui.Dot(Theme.InkMute, 8);
    private readonly TextBlock _serverText = Ui.Text("Connexion au serveur…", 12.5, Theme.InkDim, FontWeights.Medium);
    private readonly TextBlock _serverPing = Ui.Mono("", 11, Theme.InkMute);
    private readonly Border _accountAvatarText = new();
    private readonly TextBlock _accountName = Ui.Raw("", 13, Theme.Ink, FontWeights.SemiBold);
    private readonly TextBlock _accountSub = Ui.Text("", 11.5, Theme.InkMute);
    private readonly LoopGroup _loops = new();
    private Page _page = Page.Home;
    private View? _current;
    private bool _closeConfirmed;
    private bool _shellVisible;

    public MainWindow(LauncherApp app, string? startScreen)
    {
        _app = app;
        _startScreen = startScreen;
        _login = new LoginView(app);

        Title = "COTK — Crown of the King";
        Icon = LoadIcon();
        Background = Theme.Bg;
        FontFamily = Theme.Body;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Culture.IetfLanguageTag);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

        var fit = FitScale();
        Width = DesignW * fit;
        Height = DesignH * fit;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = TopBarH,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        BuildShell();
        BuildFrame(fit);

        _app.Attach(this);
        _app.Changed += what => { if (Dispatcher.CheckAccess()) OnAppChanged(what); else Dispatcher.BeginInvoke(() => OnAppChanged(what)); };

        SourceInitialized += (_, _) => Native.StyleWindow(this, Theme.LineHiColor);
        Loaded += OnLoaded;
        Activated += (_, _) => SyncAnimationActivity();
        Deactivated += (_, _) => SyncAnimationActivity();
        StateChanged += (_, _) =>
        {
            SyncAnimationActivity();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        L.Changed += RefreshLanguage;
        Closed += (_,_) => L.Changed -= RefreshLanguage;
    }

    private static ImageSource? LoadIcon()
    {
        try
        {
            var path = Path.Combine(AppPaths.InstallDir, "cotk.ico");
            return File.Exists(path) ? System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(path)) : null;
        }
        catch { return null; }
    }

    /// <summary>Écrans plus petits que la fenêtre : on réduit tout l'habillage plutôt que de rogner.</summary>
    private static double FitScale()
    {
        var work = SystemParameters.WorkArea;
        return Math.Min(1.0, Math.Min((work.Height - 8) / DesignH, (work.Width - 8) / DesignW));
    }

    // ================================================================== construction

    private void BuildFrame(double fit)
    {
        _ = fit; // l'adaptation aux petits écrans est faite par le Viewbox (réduction uniforme)
        _frame.Width = DesignW;
        _frame.Height = DesignH;
        _frame.Background = Theme.Bg;

        _shell.Visibility = Visibility.Collapsed;
        _login!.Visibility = Visibility.Collapsed;
        _frame.Children.Add(_shell);
        _frame.Children.Add(_login);
        _frame.Children.Add(WindowButtons());
        _frame.Children.Add(_modal);
        _frame.Children.Add(_toasts);
        Content = new Viewbox { Child = _frame, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    }

    private void BuildShell()
    {
        _shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarW) });
        _shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        BuildSidebar();
        _sidebar.At(0, 0);
        _shell.Children.Add(_sidebar);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TopBarH) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.At(0, 1);

        var top = BuildTopBar();
        top.At(0);
        content.Children.Add(top);
        _viewHost.At(1);
        content.Children.Add(_viewHost);
        _shell.Children.Add(content);
    }

    private void BuildSidebar()
    {
        _sidebar.Background = Theme.SidebarGradient;
        _sidebar.Children.Add(new Border { Width = 1, Background = Theme.Line, HorizontalAlignment = HorizontalAlignment.Right });

        // marque
        var crown = Icons.Make(Icons.Crown, Theme.EmberHi, 24, 1.5);
        crown.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.EmberColor, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.65 };
        var wordmark = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        wordmark.Children.Add(Ui.Display("CROWN OF", 15, Theme.Ink, FontWeights.Bold));
        wordmark.Children.Add(Ui.Display("THE KING", 15, Theme.Ink, FontWeights.Bold).M(0, -3, 0, 0));
        var brand = Ui.HStack(crown.M(0, 0, 12, 0), wordmark);
        brand.Margin = new Thickness(26, 0, 0, 0);
        brand.VerticalAlignment = VerticalAlignment.Top;
        brand.Height = TopBarH;
        brand.Children[0].SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        _sidebar.Children.Add(brand);

        // navigation principale
        var list = new StackPanel { Margin = new Thickness(12, TopBarH + 18, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        AddNav(list, Page.Home, "Accueil", Icons.Home);
        AddNav(list, Page.Library, "Bibliothèque", Icons.Library);
        AddNav(list, Page.Matches, "Matchs", Icons.Matches);
        AddNav(list, Page.Leaderboard, "Classement", Icons.Leaderboard);
        _sidebar.Children.Add(list);

        // pied : compte + paramètres
        var foot = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 16) };
        foot.Children.Add(AccountChip());
        var settings = AddNav(foot, Page.Settings, "Paramètres", Icons.Settings, margin: 8);
        _ = settings;
        foot.Children.Add(Ui.Mono($"v{Config.Version}" + (_app.Preview ? " · APERÇU" : ""), 10.5, Theme.InkMute).M(14, 10, 0, 0));
        _sidebar.Children.Add(foot);

        // indicateur animé de la page courante
        _indicator.Width = 3;
        _indicator.Height = 20;
        _indicator.CornerRadius = new CornerRadius(0, 3, 3, 0);
        _indicator.Background = Theme.EmberGradient;
        _indicator.HorizontalAlignment = HorizontalAlignment.Left;
        _indicator.VerticalAlignment = VerticalAlignment.Top;
        _indicator.RenderTransform = _indicatorMove;
        _indicator.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.EmberColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8 };
        _sidebar.Children.Add(_indicator);
        _sidebar.SizeChanged += (_, _) => PlaceIndicator(animate: false);
    }

    private NavItem AddNav(Panel host, Page page, string text, string icon, double margin = 4)
    {
        var item = new NavItem(page, text, icon) { Margin = new Thickness(0, 0, 0, margin) };
        item.Click += (_, _) => Go(page);
        _nav[page] = item;
        host.Children.Add(item);
        return item;
    }

    private FrameworkElement AccountChip()
    {
        var avatar = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Theme.EmberGradient,
            Child = _accountAvatarText,
        };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
        info.Children.Add(_accountName);
        info.Children.Add(_accountSub);
        var row = Ui.Columns(Ui.Auto, Ui.Star());
        avatar.At(0, 0);
        info.At(0, 1);
        row.Children.Add(avatar);
        row.Children.Add(info);

        var hover = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
        var chip = new Border
        {
            Background = hover, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 9, 10, 9), Child = row, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 0),
        };
        chip.MouseEnter += (_, _) => Anim.ColorTo(hover, Color.FromArgb(22, 255, 255, 255), 120);
        chip.MouseLeave += (_, _) => Anim.ColorTo(hover, Color.FromArgb(10, 255, 255, 255), 160);
        chip.MouseLeftButtonUp += (_, _) => Go(Page.Settings);
        return chip;
    }

    private FrameworkElement BuildTopBar()
    {
        _title.Inlines.Add(_titleLight);
        _title.Inlines.Add(_titleStrong);
        _title.FontSize = 17;
        _title.FontFamily = Theme.Body;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _titleLight.Foreground = Theme.InkDim;
        _titleStrong.Foreground = Theme.Ink;
        _titleStrong.FontWeight = FontWeights.SemiBold;

        var pill = new Border
        {
            Background = Theme.White04, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(13, 6, 14, 7), VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.HStack(_serverDot.M(0, 0, 9, 0), _serverText, _serverPing.M(10, 1, 0, 0)),
        };
        Anim.Pulse(_serverDot, 0.4, 1, 1700, _loops);

        var grid = Ui.Columns(Ui.Star(), Ui.Auto);
        _title.Margin = new Thickness(30, 0, 0, 0);
        _title.At(0, 0);
        pill.Margin = new Thickness(0, 0, 220, 0);
        pill.At(0, 1);
        grid.Children.Add(_title);
        grid.Children.Add(pill);
        return grid;
    }

    private FrameworkElement WindowButtons()
    {
        var minimize = new FxButton("", ButtonKind.Ghost, Icons.Minimize, 34, 12) { Width = 40 };
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        var close = new FxButton("", ButtonKind.Ghost, Icons.Close, 34, 12) { Width = 40 };
        close.Click += (_, _) => Close();
        System.Windows.Automation.AutomationProperties.SetName(minimize, L.T("Réduire"));
        System.Windows.Automation.AutomationProperties.SetName(close, L.T("Fermer"));
        WindowChrome.SetIsHitTestVisibleInChrome(minimize, true);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        var panel = Ui.HStack(new LanguagePicker().M(0,0,14,0), minimize, close.M(2, 0, 0, 0));
        panel.HorizontalAlignment = HorizontalAlignment.Right;
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.Margin = new Thickness(0, 12, 14, 0);
        return panel;
    }

    // ================================================================== navigation

    public void Go(Page page)
    {
        if (_app.Session is null) return;
        var next = GetView(page);
        if (ReferenceEquals(_current, next)) return;

        if (_current is not null)
        {
            _current.Deactivated();
            _current.Visibility = Visibility.Collapsed;
        }
        _current = next;
        _page = page;
        next.Visibility = Visibility.Visible;
        next.Activated();
        foreach (var (target, item) in _nav) item.IsSelected = target == page;
        PlaceIndicator(animate: true);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            _sidebar.UpdateLayout();
            PlaceIndicator(animate: false);
        });
        UpdateTitle(animate: true);
    }

    private View GetView(Page page)
    {
        if (_views.TryGetValue(page, out var existing)) return existing;
        View view = page switch
        {
            Page.Library => new LibraryView(_app, this),
            Page.Matches => new MatchesView(_app, this),
            Page.Leaderboard => new LeaderboardView(_app, this),
            Page.Settings => new SettingsView(_app, this),
            _ => new HomeView(_app, this),
        };
        view.Visibility = Visibility.Collapsed;
        _views[page] = view;
        _viewHost.Children.Add(view);
        return view;
    }

    private void PlaceIndicator(bool animate)
    {
        if (!_nav.TryGetValue(_page, out var item) || !item.IsLoaded) return;
        if (_sidebar.ActualHeight <= 0 || item.ActualHeight <= 0)
        {
            _indicator.Visibility = Visibility.Collapsed;
            return;
        }
        _indicator.Visibility = Visibility.Visible;
        var y = item.TranslatePoint(new Point(0, 0), _sidebar).Y + (item.ActualHeight - _indicator.Height) / 2;
        if (animate) Anim.To(_indicatorMove, TranslateTransform.YProperty, y, 520, 0, Anim.Expo);
        else
        {
            _indicatorMove.BeginAnimation(TranslateTransform.YProperty, null);
            _indicatorMove.Y = y;
        }
    }

    private void UpdateTitle(bool animate)
    {
        if (_current is null) return;
        _titleLight.Text = L.T(_current.Title);
        _titleStrong.Text = _current.TitleStrong ?? "";
        _titleLight.Foreground = _current.TitleStrong is null ? Theme.Ink : Theme.InkDim;
        _titleLight.FontWeight = _current.TitleStrong is null ? FontWeights.SemiBold : FontWeights.Normal;
        if (animate) Anim.Rise(_title, 0, 8, 360);
    }

    // ================================================================== états

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Opacity = 0;
        var scale = new ScaleTransform(0.985, 0.985, DesignW / 2, DesignH / 2);
        _frame.RenderTransform = scale;
        Anim.Fade(this, 1, 380);
        Anim.To(scale, ScaleTransform.ScaleXProperty, 1, 520, 0, Anim.Expo);
        Anim.To(scale, ScaleTransform.ScaleYProperty, 1, 520, 0, Anim.Expo);

        _login!.Visibility = Visibility.Visible;
        _login.Show();
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            await _app.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error("démarrage du launcher", ex);
            ShowError(Errors.From(ex), null);
        }
    }

    private void OnAppChanged(string what)
    {
        switch (what)
        {
            case "session":
                if (_app.Session is not null) ShowShell();
                else ShowLogin();
                UpdateAccount();
                break;
            case "server":
                UpdateServerPill();
                break;
            case "profile":
                if (_current is HomeView) UpdateTitle(animate: false);
                break;
        }
    }

    private void ShowShell()
    {
        if (_shellVisible) return;
        _shellVisible = true;
        _loops.Active = true;
        _shell.Visibility = Visibility.Visible;
        UpdateAccount();
        UpdateServerPill();

        _shell.Opacity = 0;
        Anim.Fade(_shell, 1, 320);
        var login = _login;
        if (login is not null)
            Anim.Fade(login, 0, 280, 0, () =>
            {
                if (!_shellVisible) return;
                login.ReleaseResources();
                _frame.Children.Remove(login);
                if (ReferenceEquals(_login, login)) _login = null;
            });

        _current = null;
        Go(_startScreen switch
        {
            "library" or "install" or "update" or "installing" or "uninstall" => Page.Library,
            "matches" => Page.Matches,
            "leaderboard" => Page.Leaderboard,
            "settings" => Page.Settings,
            _ => Page.Home,
        });
        PlaceIndicator(animate: false);
        RunPreviewScript();
    }

    private void ShowLogin()
    {
        if (!_shellVisible && _login?.Visibility == Visibility.Visible)
        {
            return;
        }
        _shellVisible = false;
        _current?.Deactivated();
        _current = null;
        foreach (var view in _views.Values) view.Visibility = Visibility.Collapsed;
        if (_login is null)
        {
            _login = new LoginView(_app);
            _frame.Children.Insert(1, _login);
        }
        _login.Visibility = Visibility.Visible;
        _login.Opacity = 0;
        _login.Show();
        Anim.Fade(_login, 1, 320);
        Anim.Fade(_shell, 0, 240, 0, () => _shell.Visibility = Visibility.Collapsed);
    }

    private void UpdateAccount()
    {
        var account = _app.Session?.Account;
        var name = account?.Username ?? "";
        _accountAvatarText.Child = Ui.Display(name.Length > 0 ? name[..1].ToUpperInvariant() : "?", 15, Brushes.White).Align(HorizontalAlignment.Center);
        _accountName.Text = name;
        _accountSub.Text = account is { IsAdmin: true } ? "Administrateur" : account is { IsStaff: true } ? "Modérateur" : "Survivant";
    }

    private void UpdateServerPill()
    {
        if(_app.GameStatus is {Maintenance:true})
        {
            _serverDot.Background=Theme.Gold; _serverText.Text="Maintenance en cours"; _serverText.Foreground=Theme.Gold; _serverPing.Text=""; return;
        }
        switch (_app.ServerOnline)
        {
            case true:
                _serverDot.Background = Theme.Ok;
                _serverText.Text = "Serveur de jeu en ligne";
                _serverText.Foreground = Theme.Ink;
                _serverPing.Text = $"{_app.GameStatus?.Players ?? 0} joueurs";
                break;
            case false:
                _serverDot.Background = Theme.Err;
                _serverText.Text = _app.PingMs is null ? "Connexion impossible" : "Serveur de jeu hors ligne";
                _serverText.Foreground = Theme.Err;
                _serverPing.Text = "";
                break;
            default:
                _serverDot.Background = Theme.InkMute;
                _serverText.Text = _app.PingMs is null ? "Vérification des services…" : "État du jeu inconnu";
                _serverText.Foreground = Theme.InkDim;
                _serverPing.Text = "";
                break;
        }
    }

    public void ApplyMotionSetting()
    {
        Ticker.Refresh();
        LoopGroup.RecomputeAll();
    }

    private void RefreshLanguage()
    {
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Culture.IetfLanguageTag);
        UpdateTitle(false);
    }

    private void SyncAnimationActivity()
    {
        var paused = !IsActive || WindowState == WindowState.Minimized;
        Ticker.Paused = paused;
        Anim.Suspended = paused;
        Log.Info($"animations : paused={paused} window={WindowState} active={IsActive}");
    }

    // ================================================================== clavier / fermeture

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_modal.IsOpen)
        {
            _modal.EnsureFocus();
            _modal.HandleKey(e);
            return;
        }
        if (_app.Session is null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var target = e.Key switch
        {
            Key.D1 => Page.Home,
            Key.D2 => Page.Library,
            Key.D3 => Page.Matches,
            Key.D4 => Page.Leaderboard,
            Key.D5 => Page.Settings,
            _ => (Page?)null,
        };
        if (target is { } page)
        {
            e.Handled = true;
            Go(page);
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeConfirmed || !_app.IsBusy) { _closeConfirmed = true; _app.Dispose(); return; }

        e.Cancel = true;
        if(_app.Op==Operation.Uninstalling && _app.Transfer?.Phase==TransferPhase.Uninstalling)
        {
            Toast(ToastKind.Warning,"Désinstallation en cours","Attendez la fin de la désinstallation avant de fermer le launcher."); return;
        }
        var playing = _app.Op is Operation.Launching or Operation.Playing;
        var quit = await Confirm(
            playing ? "Fermer le launcher ?" : "Interrompre l'opération ?",
            playing
                ? "Le jeu continuera, mais sans le launcher votre session ne sera pas renouvelée au retour au lobby : vous devrez le relancer pour rejouer."
                : _app.Op==Operation.Uninstalling ? "La désinstallation n'a pas commencé. Aucun fichier n'a été supprimé." : "Le téléchargement s'arrête. Il reprendra exactement où il en était à la prochaine ouverture.",
            "Fermer quand même", "Rester", icon: Icons.Warning);
        if (!quit) return;
        _closeConfirmed = true;
        _app.CancelOperation();
        Close();
    }

    // ================================================================== IShell

    public void Toast(ToastKind kind, string title, string? message = null, string? code = null, Action? action = null, string? actionLabel = null)
        => _toasts.Show(kind, title, message, code, action, actionLabel);

    public void ShowError(LauncherError error, Action? retry)
        => Dialogs.Error(_modal, error, retry, () => _app.ExportDiagnostic(), () => OpenSupportReport(error.Code));

    public Task<bool> Confirm(string title, string message, string confirm, string cancel = "Annuler", string? detail = null, string icon = Icons.Download)
        => Dialogs.Confirm(_modal, title, message, confirm, cancel, detail, icon);

    public Task<string?> PickFolder(string description)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.T(description), Multiselect = false };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(dialog.FolderName);
    }

    public void SetWindowMinimized(bool minimized)
    {
        WindowState = minimized ? WindowState.Minimized : WindowState.Normal;
        if (!minimized) Activate();
    }

    public void OpenNote(PatchNote note) => Dialogs.Note(_modal, note);

    public void OpenSupportReport(string? errorCode = null) => SupportDialogs.Submit(_modal, _app, errorCode);

    public void OpenReceivedReports() => _ = SupportDialogs.ReadReceived(_modal, _app);

    // ================================================================== aperçu : scénarios de capture

    private async void RunPreviewScript()
    {
        if (!_app.Preview || _startScreen is null) return;
        await Task.Delay(1100);
        switch (_startScreen)
        {
            case "install": _app.PreviewSetClient(ClientState.NotInstalled); break;
            case "update": _app.PreviewSetClient(ClientState.UpdateAvailable); break;
            case "installing": _app.PreviewSetClient(ClientState.NotInstalled); _ = _app.InstallOrUpdateAsync(); break;
            case "playing": _ = _app.PlayAsync(); break;
            case "error": ShowError(Errors.ClientCrashedAtStart(8, "0xC0000005"), () => { }); break;
            case "report": OpenSupportReport("E-GAME-004"); break;
            case "uninstall": _ = _app.UninstallClientAsync(); break;
            case "note": if (_app.Notes.Value is { Count: > 0 } notes) OpenNote(notes[0]); break;
            case "toast":
                Toast(ToastKind.Success, "Client mis à jour", "0.23.4.161178 → 0.23.4.161181 : 14 fichiers, 312,4 Mo.");
                Toast(ToastKind.Warning, "Versions indisponibles", "Le launcher n'a pas pu lire la liste des versions publiées.", "E-UPD-001", () => { }, "Réessayer");
                break;
        }
    }
}
