using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Écran de connexion plein cadre. Voie principale : « Se connecter avec le navigateur » (code
/// à saisir sur le site — le mot de passe ne transite jamais par le launcher). Voie de secours :
/// identifiant + mot de passe. Chaque échec affiche un code, une cause et une action.</summary>
internal sealed class LoginView : Grid
{
    private readonly LauncherApp _app;
    private readonly IScene _scene = SceneFactory.Create("login", 1120, 700, 0.42, 0.48);
    private readonly LoopGroup _loops = new();
    private readonly ContentControl _swap = new();
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _countdown;
    private bool _passwordMode;

    public LoginView(LauncherApp app)
    {
        _app = app;
        _loops.Active = false;
        _scene.Group.Active = false;
        Background = Theme.Bg;

        Children.Add(_scene.Element);
        Children.Add(new Embers(46, 1.15, 3) { IsHitTestVisible = false });
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = Theme.Gradient(0, 0, 1, 0, (0, Color.FromArgb(246, 6, 6, 8)), (0.42, Color.FromArgb(214, 6, 6, 8)), (0.8, Color.FromArgb(70, 6, 6, 8)), (1, Color.FromArgb(30, 6, 6, 8))),
        });
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = Theme.Gradient(0, 1, 0, 0, (0, Color.FromArgb(160, 6, 6, 8)), (0.35, Color.FromArgb(0, 6, 6, 8))),
        });

        MouseMove += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (ActualWidth > 0 && ActualHeight > 0) _scene.Pointer(p.X / ActualWidth * 2 - 1, p.Y / ActualHeight * 2 - 1);
        };

        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(84, 0, 0, 0), Width = 430 };
        column.Children.Add(Brand());
        _swap.Margin = new Thickness(0, 30, 0, 0);
        column.Children.Add(_swap);
        Children.Add(column);

        var footer = Ui.Mono($"LAUNCHER {Config.Version}   ·   COTK.FR" + (app.Preview ? "   ·   APERÇU / DONNÉES FICTIVES" : ""), 10.5, Theme.InkMute);
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.VerticalAlignment = VerticalAlignment.Bottom;
        footer.Margin = new Thickness(0, 0, 28, 22);
        Children.Add(footer);

        _app.Changed += HandleAppChanged;
        ShowChoice(initial: true);
    }

    /// <summary>Appelé quand l'écran devient visible : réveille les effets et rejoue l'entrée.</summary>
    public void Show()
    {
        _scene.Group.Active = true;
        _loops.Active = true;
        if (_swap.Content is UIElement content) Anim.Rise(content, 100, 18, 700);
    }

    public void Hide()
    {
        _scene.Group.Active = false;
        _loops.Active = false;
        CancelBrowser();
    }

    private void HandleAppChanged(string what)
    {
        if (what == "session") Dispatcher.BeginInvoke(OnSessionChanged);
    }

    public void ReleaseResources()
    {
        Hide();
        _app.Changed -= HandleAppChanged;
        _scene.Group.Release();
        _loops.Release();
    }

    private void OnSessionChanged()
    {
        if (_app.Session is not null) { CancelBrowser(); return; }
        // Retour à l'écran de connexion (déconnexion, session expirée) : on repart propre.
        _passwordMode = false;
        ShowChoice(initial: false);
    }

    private static FrameworkElement Brand()
    {
        var crown = Icons.Make(Icons.Crown, Theme.WithAlpha(Theme.EmberHiColor, 255), 46, 1.3);
        crown.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.EmberColor, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.8 };
        var title = Ui.Display("CROWN OF THE KING", 46, Brushes.White, FontWeights.Bold);
        title.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.65 };
        var panel = new StackPanel();
        panel.Children.Add(crown.Align(HorizontalAlignment.Left));
        panel.Children.Add(Ui.Mono(Ui.Spaced("Pré-saison 3  ·  battle royale"), 10.5, Theme.EmberHi).M(0, 14, 0, 0));
        panel.Children.Add(title.M(0, 6, 0, 0));
        panel.Children.Add(Ui.Text("Connectez-vous pour rejoindre le combat.", 14, Theme.WithAlpha(Color.FromRgb(214, 214, 224), 255)).M(0, 6, 0, 0));
        return panel;
    }

    // ================================================================== étape 1 : choix

    private void ShowChoice(bool initial)
    {
        CancelBrowser();
        var card = GlassCard();
        var body = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };

        if (_app.Preview && Environment.GetCommandLineArgs().Contains("--screen=password")) _passwordMode = true;

        if (_app.Restoring)
        {
            body.Children.Add(Ui.HStack(new Skeleton(18, 18, 9).M(0, 0, 12, 0), Ui.Text("Vérification de votre session…", 14, Theme.InkDim)));
            card.Child = body;
            _swap.Content = card;
            return; // « session » sera levé à la fin de la restauration : OnSessionChanged reconstruit l'écran
        }

        if (!string.IsNullOrWhiteSpace(_app.SignOutReason))
            body.Children.Add(Banner(Icons.Info, Theme.InfoColor, _app.SignOutReason!));

        var browser = new FxButton("Se connecter avec le navigateur", ButtonKind.Primary, Icons.Globe, 54, 15);
        browser.Click += (_, _) => _ = StartBrowserLoginAsync();
        body.Children.Add(browser.M(0, body.Children.Count > 0 ? 14 : 0, 0, 0));
        body.Children.Add(Ui.Text("Recommandé : vous validez sur cotk.fr, votre mot de passe ne passe jamais par le launcher.", 12, Theme.InkMute, wrap: true).M(2, 10, 2, 0));

        body.Children.Add(Divider("ou").M(0, 18, 0, 18));

        if (_passwordMode)
        {
            body.Children.Add(PasswordForm());
        }
        else
        {
            var usePassword = new FxButton("Identifiant et mot de passe", ButtonKind.Secondary, Icons.Key, 44, 13.5);
            usePassword.Click += (_, _) => { _passwordMode = true; ShowChoice(initial: false); };
            body.Children.Add(usePassword);
        }

        var signup = Ui.Text("Pas encore de compte ?  Créer un compte sur cotk.fr  →", 12.5, Theme.EmberHi, FontWeights.SemiBold);
        signup.Cursor = Cursors.Hand;
        signup.MouseEnter += (_, _) => signup.TextDecorations = TextDecorations.Underline;
        signup.MouseLeave += (_, _) => signup.TextDecorations = null;
        signup.MouseLeftButtonUp += (_, _) => _app.OpenUrl(Config.SiteUrl + "/register");
        body.Children.Add(signup.M(2, 20, 0, 0));

        card.Child = body;
        _swap.Content = card;
        if (!initial) Anim.Rise(card, 0, 14, 420);
    }

    // ================================================================== connexion par le navigateur

    private async Task StartBrowserLoginAsync()
    {
        CancelBrowser();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var card = GlassCard();
        var wait = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };
        wait.Children.Add(Ui.HStack(new Skeleton(18, 18, 9).M(0, 0, 12, 0), Ui.Text("Connexion au service COTK…", 14, Theme.InkDim)));
        card.Child = wait;
        _swap.Content = card;
        Anim.Rise(card, 0, 10, 300);

        try
        {
            await _app.LoginWithBrowserAsync(start => Dispatcher.Invoke(() => ShowCode(start)), ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            var error = Errors.From(ex);
            Log.Warn($"connexion navigateur : {error.Code}", ex);
            ShowChoiceWithError(error);
        }
    }

    private void ShowCode(DeviceStart start)
    {
        var card = GlassCard();
        var body = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };

        body.Children.Add(Ui.Mono(Ui.Spaced("Étape 1  ·  ouvrez le site"), 10.5, Theme.EmberHi));
        body.Children.Add(Ui.Text("Entrez ce code sur cotk.fr pour autoriser le launcher :", 13.5, Theme.InkDim, wrap: true).M(0, 8, 0, 0));

        var codeBox = new Border
        {
            Background = Theme.WithAlpha(Colors.Black, 110), BorderBrush = Theme.WithAlpha(Theme.EmberColor, 110), BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(0, 14, 0, 14), Margin = new Thickness(0, 14, 0, 0),
        };
        var code = Ui.Display(start.UserCode, 40, Brushes.White, FontWeights.Bold);
        code.HorizontalAlignment = HorizontalAlignment.Center;
        code.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.EmberColor, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.35 };
        codeBox.Child = code;
        body.Children.Add(codeBox);

        var actions = Ui.Columns(Ui.Star(), Ui.Star());
        var open = new FxButton("Ouvrir le site", ButtonKind.Secondary, Icons.External, 40, 13);
        var url = $"{start.VerificationUri}?code={Uri.EscapeDataString(start.UserCode)}";
        open.Click += (_, _) => _app.OpenUrl(url);
        var copy = new FxButton("Copier le code", ButtonKind.Secondary, Icons.Copy, 40, 13);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(start.UserCode); copy.Text = "Copié ✓"; } catch { copy.Text = "Copie impossible"; }
        };
        open.Margin = new Thickness(0, 0, 5, 0);
        copy.Margin = new Thickness(5, 0, 0, 0);
        open.At(0, 0);
        copy.At(0, 1);
        actions.Children.Add(open);
        actions.Children.Add(copy);
        body.Children.Add(actions.M(0, 12, 0, 0));

        // ---- attente + compte à rebours ----
        var bar = new ProgressBarFx { Height = 4, Margin = new Thickness(0, 22, 0, 0) };
        bar.Fraction = 1;
        var remaining = Ui.Mono("", 11.5, Theme.InkDim);
        var status = Ui.HStack(Ui.Dot(Theme.Ember, 8).M(0, 0, 10, 0), Ui.Text("En attente de votre validation…", 13, Theme.Ink, FontWeights.SemiBold));
        Anim.Pulse((UIElement)status.Children[0], 0.3, 1, 1200, _loops);
        var line = Ui.Columns(Ui.Star(), Ui.Auto);
        status.At(0, 0);
        remaining.At(0, 1);
        line.Children.Add(status);
        line.Children.Add(remaining);
        body.Children.Add(line.M(0, 14, 0, 0));
        body.Children.Add(bar);

        var cancel = new FxButton("Annuler", ButtonKind.Ghost, null, 36, 13) { HorizontalAlignment = HorizontalAlignment.Left };
        cancel.Click += (_, _) => ShowChoice(initial: false);
        body.Children.Add(cancel.M(-8, 14, 0, 0));

        card.Child = body;
        _swap.Content = card;
        Anim.Rise(card, 0, 12, 380);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, start.ExpiresIn));
        var total = (deadline - DateTimeOffset.UtcNow).TotalSeconds;
        _countdown?.Stop();
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            var left = deadline - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero) { _countdown?.Stop(); return; }
            remaining.Text = $"expire dans {(int)left.TotalMinutes}:{left.Seconds:00}";
            bar.SetFraction(left.TotalSeconds / total, 900);
        };
        _countdown.Start();
        remaining.Text = $"expire dans {start.ExpiresIn / 60}:{start.ExpiresIn % 60:00}";
        _app.OpenUrl(url);
    }

    private void CancelBrowser()
    {
        _countdown?.Stop();
        _countdown = null;
        if (_cts is null) return;
        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
    }

    // ================================================================== identifiant + mot de passe

    private FrameworkElement PasswordForm()
    {
        var user = new FxInput("Pseudo ou e-mail", Icons.User);
        var password = new FxInput("Mot de passe", Icons.Lock, password: true);
        var error = new ContentControl();
        var submit = new FxButton("Se connecter", ButtonKind.Primary, null, 48, 15);
        var submitting = false;

        async Task Submit()
        {
            if (submitting) return;
            var name = user.Value.Trim();
            if (name.Length == 0 || password.Value.Length == 0)
            {
                user.SetError(name.Length == 0);
                password.SetError(password.Value.Length == 0);
                error.Content = Banner(Icons.Warning, Theme.GoldColor, "Saisissez votre identifiant et votre mot de passe.");
                (name.Length == 0 ? user : password).FocusInput();
                return;
            }

            error.Content = null;
            submitting = true;
            submit.IsBusy = true;
            user.InputEnabled = false;
            password.InputEnabled = false;
            try
            {
                await _app.LoginWithPasswordAsync(name, password.Value, CancellationToken.None);
            }
            catch (Exception ex)
            {
                var failure = Errors.From(ex);
                Log.Warn($"connexion par mot de passe : {failure.Code}", ex);
                submit.IsBusy = false;
                user.InputEnabled = true;
                password.InputEnabled = true;
                password.Value = string.Empty;
                if (failure.Code == "E-AUTH-001") { user.SetError(true); password.SetError(true); }
                error.Content = ErrorBanner(failure);
                password.FocusInput();
            }
            finally { submitting = false; }
        }

        submit.Click += async (_, _) => await Submit();
        user.Submitted += () => password.FocusInput();
        password.Submitted += async () => await Submit();

        var back = new FxButton("Utiliser le navigateur plutôt", ButtonKind.Ghost, null, 34, 12.5) { HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += (_, _) => { _passwordMode = false; ShowChoice(initial: false); };

        var panel = new StackPanel();
        panel.Children.Add(user);
        panel.Children.Add(password.M(0, 10, 0, 0));
        panel.Children.Add(error.M(0, 12, 0, 0));
        panel.Children.Add(submit.M(0, 14, 0, 0));
        panel.Children.Add(back.M(-8, 6, 0, 0));
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => user.FocusInput());
        return panel;
    }

    private void ShowChoiceWithError(LauncherError error)
    {
        ShowChoice(initial: false);
        if (_swap.Content is Border { Child: StackPanel body })
            body.Children.Insert(0, ErrorBanner(error).M(0, 0, 0, 16));
    }

    // ================================================================== éléments

    private static Border GlassCard() => new()
    {
        Background = Theme.WithAlpha(Color.FromRgb(14, 14, 18), 214),
        BorderBrush = Theme.White12,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(18),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 50, ShadowDepth = 14, Direction = 270, Opacity = 0.55 },
    };

    private static FrameworkElement Divider(string text)
    {
        var grid = Ui.Columns(Ui.Star(), Ui.Auto, Ui.Star());
        grid.Children.Add(Ui.Rule().At(0, 0).Align(HorizontalAlignment.Stretch));
        grid.Children.Add(Ui.Text(text, 11.5, Theme.InkMute).M(12, 0, 12, 0).At(0, 1));
        grid.Children.Add(Ui.Rule().At(0, 2).Align(HorizontalAlignment.Stretch));
        return grid;
    }

    private static Border Banner(string icon, Color color, string text) => new()
    {
        Background = Theme.WithAlpha(color, 22),
        BorderBrush = Theme.WithAlpha(color, 70),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(13, 10, 13, 11),
        Child = Ui.Columns(Ui.Px(28), Ui.Star()).Also(g =>
        {
            g.Children.Add(Icons.Make(icon, Theme.WithAlpha(color, 255), 16, 1.8).Align(HorizontalAlignment.Left, VerticalAlignment.Top).M(0, 2, 0, 0).At(0, 0));
            g.Children.Add(Ui.Text(text, 12.5, Theme.Ink, wrap: true).At(0, 1));
        }),
    };

    private static Border ErrorBanner(LauncherError error)
    {
        var panel = new StackPanel();
        var head = Ui.Columns(Ui.Star(), Ui.Auto);
        head.Children.Add(Ui.Text(error.Title, 13, Theme.Ink, FontWeights.Bold).At(0, 0));
        head.Children.Add(Ui.Mono(error.Code, 10.5, Theme.Err).At(0, 1));
        panel.Children.Add(head);
        panel.Children.Add(Ui.Text(error.Message, 12.5, Theme.InkDim, wrap: true).M(0, 3, 0, 0));
        if (error.Hint is not null) panel.Children.Add(Ui.Text(error.Hint, 12, Theme.InkMute, wrap: true).M(0, 4, 0, 0));
        return new Border
        {
            Background = Theme.WithAlpha(Theme.ErrColor, 22),
            BorderBrush = Theme.WithAlpha(Theme.ErrColor, 80),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11, 14, 12),
            Child = panel,
        };
    }
}

internal static class GridExtensions
{
    public static Grid Also(this Grid grid, Action<Grid> configure)
    {
        configure(grid);
        return grid;
    }
}
