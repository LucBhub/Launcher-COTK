using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using COTK.Next.Core;

namespace COTK.Next.UI;

internal enum ToastKind { Info, Success, Warning, Error }

/// <summary>Notifications empilées en bas à droite : glissent, se ferment seules (sauf erreurs), barre de temps
/// qui se vide, pause au survol. Jamais de MessageBox système.</summary>
internal sealed class ToastHost : Grid
{
    private readonly StackPanel _stack = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right };

    public ToastHost()
    {
        IsHitTestVisible = true;
        Background = null;
        _stack.Margin = new Thickness(0, 0, 22, 22);
        Children.Add(_stack);
    }

    public void Show(ToastKind kind, string title, string? message = null, string? code = null,
        Action? action = null, string? actionLabel = null, int? durationMs = null)
    {
        while (_stack.Children.Count >= 3) Dismiss((FrameworkElement)_stack.Children[0], immediate: true);

        var accent = kind switch
        {
            ToastKind.Success => Theme.OkColor,
            ToastKind.Warning => Theme.GoldColor,
            ToastKind.Error => Theme.ErrColor,
            _ => Theme.InfoColor,
        };
        var accentBrush = Theme.WithAlpha(accent, 255);
        var glyph = kind switch { ToastKind.Success => Icons.Check, ToastKind.Warning => Icons.Warning, ToastKind.Error => Icons.Warning, _ => Icons.Info };
        var duration = durationMs ?? kind switch { ToastKind.Error => 12000, ToastKind.Warning => 8000, _ => 4500 };

        var card = new Card(12) { Width = 372, Margin = new Thickness(0, 10, 0, 0), Background = Theme.CardHi, BorderBrush = Theme.WithAlpha(accent, 70) };
        var grid = Ui.Columns(Ui.Px(3), Ui.Px(48), Ui.Star(), Ui.Auto);
        grid.Children.Add(new Border { Background = accentBrush, CornerRadius = new CornerRadius(12, 0, 0, 12) }.At(0, 0));

        var badge = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Theme.WithAlpha(accent, 36),
            Child = Icons.Make(glyph, accentBrush, 15, 2).Align(HorizontalAlignment.Center),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
        };
        badge.At(0, 1);
        grid.Children.Add(badge);

        var body = new StackPanel { Margin = new Thickness(0, 13, 8, 14) };
        body.Children.Add(Ui.Text(title, 13.5, Theme.Ink, FontWeights.SemiBold, wrap: true));
        if (!string.IsNullOrWhiteSpace(message)) body.Children.Add(Ui.Text(message, 12.5, Theme.InkDim, wrap: true).M(0, 3, 0, 0));
        if (code is not null || action is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
            if (code is not null)
            {
                row.Children.Add(new Border
                {
                    Background = Theme.White08, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 2),
                    Child = Ui.Mono(code, 10.5, Theme.InkDim), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                });
            }
            if (action is not null)
            {
                var link = new FxButton(actionLabel ?? "Voir", ButtonKind.Ghost, null, 26, 12);
                link.Click += (_, _) => { action(); Dismiss(card); };
                row.Children.Add(link);
            }
            body.Children.Add(row);
        }
        body.At(0, 2);
        grid.Children.Add(body);

        var close = new FxButton("", ButtonKind.Ghost, Icons.Close, 28, 12) { Width = 30 };
        close.Margin = new Thickness(0, 6, 6, 0);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => Dismiss(card);
        close.At(0, 3);
        grid.Children.Add(close);

        // barre de temps en pied de carte
        var timer = new Border { Height = 2, Background = Theme.WithAlpha(accent, 150), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, RenderTransformOrigin = new Point(0, 0.5) };
        var scale = new ScaleTransform(1, 1);
        timer.RenderTransform = scale;
        var layered = new Grid();
        layered.Children.Add(grid);
        layered.Children.Add(timer.At(columnSpan: 1));
        timer.Width = 372;
        card.Child = layered;
        card.ClipToBounds = true;

        _stack.Children.Add(card);
        var slide = new TranslateTransform(60, 0);
        card.RenderTransform = slide;
        card.Opacity = 0;
        Anim.Fade(card, 1, 260);
        Anim.To(slide, TranslateTransform.XProperty, 0, 420, 0, Anim.Expo);

        if (kind != ToastKind.Error || durationMs is not null)
        {
            Anim.To(scale, ScaleTransform.ScaleXProperty, 0, duration, 0, Anim.Linear);
            var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(duration) };
            clock.Tick += (_, _) => { clock.Stop(); Dismiss(card); };
            card.MouseEnter += (_, _) => clock.Stop();
            card.MouseLeave += (_, _) => clock.Start();
            clock.Start();
        }
        else
        {
            timer.Visibility = Visibility.Collapsed;
        }
    }

    private void Dismiss(FrameworkElement card, bool immediate = false)
    {
        if (!_stack.Children.Contains(card)) return;
        if (immediate || !Anim.Enabled)
        {
            _stack.Children.Remove(card);
            return;
        }
        Anim.Fade(card, 0, 200, 0, () => _stack.Children.Remove(card));
    }
}

/// <summary>Fenêtre modale intégrée : fond assombri, carte centrée qui s'ouvre en douceur. Échap / clic dehors
/// ferment (sauf modale bloquante).</summary>
internal sealed class ModalHost : Grid
{
    private readonly Border _scrim = new() { Background = Theme.Scrim, Opacity = 0 };
    private readonly ContentPresenter _holder = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private bool _dismissible = true;
    private Action? _closed;
    private IInputElement? _previousFocus;

    public bool IsOpen { get; private set; }

    public ModalHost()
    {
        Visibility = Visibility.Collapsed;
        Children.Add(_scrim);
        Children.Add(_holder);
        KeyboardNavigation.SetTabNavigation(_holder, KeyboardNavigationMode.Cycle);
        _scrim.MouseLeftButtonDown += (_, _) => { if (_dismissible) Hide(); };
    }

    public void Show(FrameworkElement content, bool dismissible = true, Action? closed = null)
    {
        if (IsOpen) Hide(immediate: true);
        _closed = closed;
        _dismissible = dismissible;
        IsOpen = true;
        Visibility = Visibility.Visible;
        _holder.Content = content;
        _previousFocus = Keyboard.FocusedElement;
        content.Focusable = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (IsOpen && ReferenceEquals(_holder.Content, content)) Keyboard.Focus(content);
        });

        var scale = new ScaleTransform(0.94, 0.94);
        var slide = new TranslateTransform(0, 14);
        content.RenderTransformOrigin = new Point(0.5, 0.5);
        content.RenderTransform = new TransformGroup { Children = { scale, slide } };
        content.Opacity = 0;
        Anim.Fade(_scrim, 1, 200);
        Anim.Fade(content, 1, 220);
        Anim.To(scale, ScaleTransform.ScaleXProperty, 1, 380, 0, Anim.Back);
        Anim.To(scale, ScaleTransform.ScaleYProperty, 1, 380, 0, Anim.Back);
        Anim.To(slide, TranslateTransform.YProperty, 0, 380, 0, Anim.Expo);
    }

    public void Hide(bool immediate = false)
    {
        if (!IsOpen) return;
        IsOpen = false;
        var closed = _closed;
        _closed = null;
        void Finish()
        {
            if (IsOpen) return;
            Visibility = Visibility.Collapsed;
            _holder.Content = null;
            _scrim.Opacity = 0;
        }

        if (immediate || !Anim.Enabled)
        {
            Finish();
        }
        else
        {
            Anim.Fade(_scrim, 0, 160, 0, Finish);
            if (_holder.Content is UIElement content) Anim.Fade(content, 0, 140);
        }
        closed?.Invoke();
        if (_previousFocus is not null) Keyboard.Focus(_previousFocus);
        _previousFocus = null;
    }

    public void EnsureFocus()
    {
        if (!IsKeyboardFocusWithin && _holder.Content is IInputElement content) Keyboard.Focus(content);
    }

    public void HandleKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsOpen && _dismissible)
        {
            e.Handled = true;
            Hide();
        }
    }
}

/// <summary>Boîtes de dialogue du launcher (toutes passent par <see cref="ModalHost"/>).</summary>
internal static class Dialogs
{
    private static Card Frame(double width, Brush? accent = null)
    {
        var card = new Card(18) { Width = width, Background = Theme.CardHi, BorderBrush = accent ?? Theme.LineHi, Padding = new Thickness(0) };
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 60, ShadowDepth = 18, Direction = 270, Opacity = 0.6 };
        return card;
    }

    public static Task<bool> Confirm(ModalHost host, string title, string message, string confirmText, string cancelText = "Annuler", string? detail = null, string icon = Icons.Download)
    {
        var tcs = new TaskCompletionSource<bool>();
        var card = Frame(460);
        var body = new StackPanel { Margin = new Thickness(28, 28, 28, 24) };

        var badge = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26), HorizontalAlignment = HorizontalAlignment.Left,
            Background = Theme.WithAlpha(Theme.EmberColor, 30), BorderBrush = Theme.WithAlpha(Theme.EmberColor, 90), BorderThickness = new Thickness(1),
            Child = Icons.Make(icon, Theme.EmberHi, 24, 1.8).Align(HorizontalAlignment.Center),
        };
        body.Children.Add(badge);
        body.Children.Add(Ui.Display(title, 22, Theme.Ink).M(0, 16, 0, 0));
        body.Children.Add(Ui.Text(message, 13.5, Theme.InkDim, wrap: true).M(0, 8, 0, 0));
        if (detail is not null)
        {
            body.Children.Add(new Border
            {
                Background = Theme.White04, CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 14, 0, 0),
                Child = Ui.Mono(detail, 11.5, Theme.InkDim),
            });
        }

        var ok = new FxButton(confirmText, ButtonKind.Primary, null, 44, 14) { MinWidth = 150 };
        var cancel = new FxButton(cancelText, ButtonKind.Ghost, null, 44, 13) { MinWidth = 100 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok.M(8, 0, 0, 0));
        body.Children.Add(buttons);
        card.Child = body;

        ok.Click += (_, _) => { tcs.TrySetResult(true); host.Hide(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(false); host.Hide(); };
        host.Show(card, dismissible: true, closed: () => tcs.TrySetResult(false));
        ok.Focus();
        return tcs.Task;
    }

    /// <summary>Erreur lisible : code stable, ce qui s'est passé, quoi faire, détail technique repliable.</summary>
    public static void Error(ModalHost host, LauncherError error, Action? retry, Action exportDiagnostic, Action? sendReport = null)
    {
        var card = Frame(720, Theme.WithAlpha(Theme.ErrColor, 90));
        var body = new StackPanel { Margin = new Thickness(30, 28, 30, 24) };

        var head = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto);
        var ring = new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Background = Theme.WithAlpha(Theme.ErrColor, 28),
            BorderBrush = Theme.WithAlpha(Theme.ErrColor, 110), BorderThickness = new Thickness(1),
            Child = Icons.Make(Icons.Warning, Theme.Err, 26, 1.8).Align(HorizontalAlignment.Center),
        };
        ring.At(0, 0);
        var glow = new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.ErrColor, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.5 }, Background = Brushes.Transparent };
        var ringHost = new Grid { Width = 54, Height = 54 };
        ringHost.Children.Add(glow);
        ringHost.Children.Add(ring);
        ringHost.At(0, 0);
        Anim.Pulse(glow, 0.3, 1, 1800);
        head.Children.Add(ringHost);
        var chip = new Border
        {
            Background = Theme.WithAlpha(Theme.ErrColor, 28), BorderBrush = Theme.WithAlpha(Theme.ErrColor, 80), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Top,
            Child = Ui.Mono(error.Code, 12, Theme.Err),
        };
        chip.At(0, 2);
        head.Children.Add(chip);
        body.Children.Add(head);

        body.Children.Add(Ui.Display(error.Title, 23, Theme.Ink).M(0, 16, 0, 0));
        body.Children.Add(Ui.Text(error.Message, 13.5, Theme.InkDim, wrap: true).M(0, 8, 0, 0));

        if (error.Hint is not null)
        {
            var hint = Ui.Columns(Ui.Px(30), Ui.Star());
            hint.Children.Add(Icons.Make(Icons.Bolt, Theme.Gold, 16, 1.8).Align(HorizontalAlignment.Left, VerticalAlignment.Top).M(0, 2, 0, 0).At(0, 0));
            hint.Children.Add(Ui.Text(error.Hint, 13, Theme.Ink, wrap: true).At(0, 1));
            body.Children.Add(new Border
            {
                Background = Theme.WithAlpha(Theme.GoldColor, 14), BorderBrush = Theme.WithAlpha(Theme.GoldColor, 50), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 16, 0, 0), Child = hint,
            });
        }

        if (error.Technical is not null)
        {
            var details = new Expander
            {
                Header = Ui.Text("Détails techniques", 12, Theme.InkMute), Margin = new Thickness(0, 14, 0, 0), Foreground = Theme.InkMute,
                Content = new Border
                {
                    Background = Theme.White04, CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 8, 11, 8), Margin = new Thickness(0, 8, 0, 0),
                    Child = Ui.Raw(error.Technical, 11.5, Theme.InkDim, font: Theme.Mono, wrap: true),
                },
            };
            body.Children.Add(details);
        }

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var close = new FxButton("Fermer", ButtonKind.Ghost, null, 42, 13);
        var copy = new FxButton("Copier le rapport", ButtonKind.Secondary, Icons.Copy, 42, 13);
        var export = new FxButton("Exporter", ButtonKind.Secondary, Icons.Folder, 42, 13);
        buttons.Children.Add(close);
        if (sendReport is not null && error.Code != "E-AUTH-001")
        {
            var support = new FxButton("Envoyer au support", ButtonKind.Secondary, Icons.External, 42, 13);
            support.Click += (_, _) => sendReport();
            buttons.Children.Add(support.M(8, 0, 0, 0));
        }
        buttons.Children.Add(export.M(8, 0, 0, 0));
        buttons.Children.Add(copy.M(8, 0, 0, 0));
        close.Click += (_, _) => host.Hide();
        export.Click += (_, _) => exportDiagnostic();
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(L.T(error.Report())); copy.Text = "Copié ✓"; }
            catch { copy.Text = "Copie impossible"; }
        };
        if (retry is not null && error.Retryable)
        {
            var again = new FxButton("Réessayer", ButtonKind.Primary, Icons.Refresh, 42, 14);
            again.Click += (_, _) => { host.Hide(); retry(); };
            buttons.Children.Add(again.M(8, 0, 0, 0));
        }
        body.Children.Add(buttons);
        card.Child = body;
        host.Show(card);
    }

    public static void Note(ModalHost host, PatchNote note)
    {
        var card = Frame(620);
        var body = new Grid { Margin = new Thickness(30, 28, 30, 24), MaxHeight = 560 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel();
        head.Children.Add(Ui.HStack(Ui.Mono(Ui.Spaced("Notes de patch"),10.5,Theme.EmberHi), Ui.Raw("  ·  " + note.PublishedAt.ToLocalTime().ToString("d MMMM yyyy", L.Culture),10.5,Theme.EmberHi)));
        head.Children.Add(Ui.Raw(note.Title, 26, Theme.Ink, FontWeights.SemiBold, font:Theme.Display).M(0, 6, 0, 0));
        head.Children.Add(Ui.Rule().M(0, 16, 0, 0));
        head.At(0);
        body.Children.Add(head);

        var text = Ui.Raw(note.Body.Replace("\r\n", "\n").Replace("\n\n", "\n \n"), 13.5, Theme.InkDim, wrap: true);
        text.LineHeight = 21;
        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = text, Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(0, 0, 12, 0) };
        scroller.At(1);
        body.Children.Add(scroller);

        var close = new FxButton("Fermer", ButtonKind.Secondary, null, 42, 13) { HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 120, Margin = new Thickness(0, 18, 0, 0) };
        close.Click += (_, _) => host.Hide();
        close.At(2);
        body.Children.Add(close);
        card.Child = body;
        host.Show(card);
    }
}
