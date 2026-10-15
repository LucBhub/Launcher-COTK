using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

internal enum Page { Home, Library, Matches, Leaderboard, Settings }

internal interface INavigator
{
    void Go(Page page);

    void OpenNote(PatchNote note);

    Task<bool> Confirm(string title, string message, string confirm, string cancel = "Annuler", string? detail = null, string icon = Icons.Download);
}

/// <summary>Page du launcher. Reste en mémoire (pas de reconstruction à chaque clic) ; <see cref="Activated"/>
/// rejoue l'entrée animée et réveille les effets, <see cref="Deactivated"/> les endort.</summary>
internal abstract class View : Grid
{
    protected readonly LauncherApp App;
    protected readonly INavigator Nav;

    /// <summary>Boucles de la vue (pulsations…) : endormies quand la vue n'est pas affichée.</summary>
    protected readonly LoopGroup Loops = new();

    protected View(LauncherApp app, INavigator nav)
    {
        App = app;
        Nav = nav;
        Loops.Active = false;
        app.Changed += OnAppChanged;
    }

    public abstract string Title { get; }

    /// <summary>Fin du titre mise en gras (ex. le pseudo après « Bon retour, »).</summary>
    public virtual string? TitleStrong => null;

    public bool IsCurrent { get; private set; }

    public virtual void Activated()
    {
        IsCurrent = true;
        Loops.Active = true;
    }

    public virtual void Deactivated()
    {
        IsCurrent = false;
        Loops.Active = false;
    }

    private void OnAppChanged(string what)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnAppChanged(what)); return; }
        Changed(what);
    }

    /// <summary>Les vues cachées se mettent à jour aussi (pas de contenu périmé au retour), mais sans animer.</summary>
    protected abstract void Changed(string what);

    // ------------------------------------------------------------------ éléments communs

    protected static ScrollViewer Scroller(UIElement content, Thickness? padding = null) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = content,
        Padding = padding ?? new Thickness(0),
        CanContentScroll = false,
    };

    protected static TextBlock SectionTitle(string text) => Ui.Text(text, 14, Theme.InkDim, FontWeights.SemiBold).M(2, 0, 0, 10);

    protected static Border Chip(string text, Brush? foreground = null, Brush? background = null, Brush? border = null, double size = 11.5)
        => new()
        {
            Background = background ?? Theme.White08,
            BorderBrush = border ?? Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 3, 9, 4),
            Child = Ui.Text(text, size, foreground ?? Theme.InkDim, FontWeights.SemiBold),
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>Clip aux coins arrondis (ClipToBounds seul clippe en rectangle).</summary>
    public static void RoundClip(FrameworkElement element, double radius)
    {
        void Apply() => element.Clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        element.SizeChanged += (_, _) => Apply();
        Apply();
    }

    protected static FrameworkElement EmptyState(string icon, string title, string text, FxButton? action = null)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 40, 0, 40), MaxWidth = 420 };
        var ring = new Border
        {
            Width = 76, Height = 76, CornerRadius = new CornerRadius(38), HorizontalAlignment = HorizontalAlignment.Center,
            Background = Theme.White04, BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
            Child = Icons.Make(icon, Theme.InkMute, 30, 1.6).Align(HorizontalAlignment.Center),
        };
        panel.Children.Add(ring);
        panel.Children.Add(Ui.Display(title, 20, Theme.Ink).M(0, 18, 0, 0).Align(HorizontalAlignment.Center));
        panel.Children.Add(Ui.Text(text, 13, Theme.InkDim, wrap: true, align: TextAlignment.Center).M(0, 8, 0, 0));
        if (action is not null) panel.Children.Add(action.M(0, 18, 0, 0).Align(HorizontalAlignment.Center));
        return panel;
    }

    protected static FrameworkElement ErrorState(LauncherError error, Action retry)
    {
        var again = new FxButton("Réessayer", ButtonKind.Secondary, Icons.Refresh, 38, 13);
        again.Click += (_, _) => retry();
        var state = EmptyState(Icons.Warning, error.Title, error.Message + (error.Hint is null ? "" : " " + error.Hint), again);
        if (state is StackPanel panel) panel.Children.Add(Ui.Mono(error.Code, 11, Theme.InkMute).M(0, 12, 0, 0).Align(HorizontalAlignment.Center));
        return state;
    }
}

/// <summary>Interrupteur animé (réglages).</summary>
internal sealed class FxToggle : Border
{
    private readonly Border _knob;
    private readonly TranslateTransform _slide = new();
    private readonly SolidColorBrush _track = new(Theme.LineHiColor);
    private bool _on;

    public event Action<bool>? Toggled;

    public FxToggle(bool initial)
    {
        Width = 46;
        Height = 26;
        CornerRadius = new CornerRadius(13);
        Background = _track;
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        _knob = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0), RenderTransform = _slide,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.4, Color = Colors.Black },
        };
        Child = _knob;
        _on = initial;
        Apply(animate: false);
        MouseLeftButtonUp += (_, _) => Flip();
        KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { Flip(); e.Handled = true; } };
    }

    public bool IsOn
    {
        get => _on;
        set { _on = value; Apply(animate: true); }
    }

    private void Flip()
    {
        _on = !_on;
        Apply(animate: true);
        Toggled?.Invoke(_on);
    }

    private void Apply(bool animate)
    {
        var x = _on ? 20 : 0;
        var color = _on ? Theme.EmberColor : Theme.LineHiColor;
        if (!animate)
        {
            _slide.X = x;
            _track.Color = color;
            return;
        }
        Anim.To(_slide, TranslateTransform.XProperty, x, 240, 0, Anim.Back);
        Anim.ColorTo(_track, color, 200);
    }
}
