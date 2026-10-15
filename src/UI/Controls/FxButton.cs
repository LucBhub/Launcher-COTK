using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Shapes = System.Windows.Shapes;

namespace COTK.Next.UI;

internal enum ButtonKind { Primary, Secondary, Ghost, Danger }

/// <summary>Bouton du launcher. C'est un vrai <see cref="Button"/> (clavier, focus, accessibilité, Entrée/Espace)
/// dont seul l'habillage est maison : survol lumineux, pression, reflet qui balaie, état « occupé ».</summary>
internal sealed class FxButton : Button
{
    internal static readonly ControlTemplate Bare = (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter/></ControlTemplate>");

    private readonly ButtonKind _kind;
    private readonly Border _root;
    private readonly Border _hover;
    private readonly Border _ring;
    private readonly Grid _row;
    private readonly TextBlock _label;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DropShadowEffect? _glow;
    private readonly Shapes.Rectangle? _sheen;
    private readonly TranslateTransform _sheenMove = new();
    private Icon? _icon;
    private bool _busy;
    private AnimationClock? _sheenClock;
    private AnimationClock? _spinClock;

    public FxButton(string text, ButtonKind kind = ButtonKind.Secondary, string? icon = null, double height = 40, double fontSize = 0)
    {
        _kind = kind;
        Template = Bare;
        Height = height;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        Focusable = true;
        Padding = new Thickness(0);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var primary = kind == ButtonKind.Primary;
        var radius = new CornerRadius(primary ? 11 : 10);

        _root = new Border
        {
            CornerRadius = radius,
            Background = kind switch
            {
                ButtonKind.Primary => Theme.EmberGradient,
                ButtonKind.Secondary => Theme.CardGradient,
                ButtonKind.Danger => Theme.WithAlpha(Theme.ErrColor, 28),
                _ => Theme.Transparent,
            },
            BorderThickness = new Thickness(kind == ButtonKind.Ghost ? 0 : 1),
            BorderBrush = kind switch
            {
                ButtonKind.Primary => Theme.Gradient(0, 0, 0, 1, (0, Color.FromArgb(120, 255, 255, 255)), (1, Color.FromArgb(24, 255, 255, 255))),
                ButtonKind.Danger => Theme.WithAlpha(Theme.ErrColor, 90),
                _ => Theme.Line,
            },
            ClipToBounds = true,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _scale,
        };

        _hover = new Border { CornerRadius = radius, Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        _ring = new Border
        {
            CornerRadius = radius,
            BorderThickness = new Thickness(2),
            BorderBrush = Theme.WithAlpha(Theme.GoldColor, 230),
            Opacity = 0,
            IsHitTestVisible = false,
            Margin = new Thickness(-1),
        };

        _label = primary
            ? Ui.Display(text.ToUpperInvariant(), fontSize > 0 ? fontSize : 15, Brushes.White, FontWeights.SemiBold)
            : Ui.Text(text, fontSize > 0 ? fontSize : 13, kind == ButtonKind.Danger ? Theme.Err : Theme.Ink, FontWeights.SemiBold);
        _label.TextTrimming = TextTrimming.CharacterEllipsis;

        _row = Ui.Columns(Ui.Auto, Ui.Star());
        _row.HorizontalAlignment = HorizontalAlignment.Center;
        _row.VerticalAlignment = VerticalAlignment.Center;
        _row.ColumnDefinitions[1].Width = Ui.Auto;
        _iconOnly = text.Length == 0;
        _row.Margin = _iconOnly ? new Thickness(0) : new Thickness(18, 0, 18, 0);
        _label.At(0, 1);
        _row.Children.Add(_label);
        if (icon is not null) SetIcon(icon);

        var layers = new Grid();
        if (primary)
        {
            _sheen = new Shapes.Rectangle
            {
                Width = 64,
                Fill = Theme.Gradient(0, 0, 1, 0, (0, Color.FromArgb(0, 255, 255, 255)), (0.5, Color.FromArgb(70, 255, 255, 255)), (1, Color.FromArgb(0, 255, 255, 255))),
                RenderTransform = new TransformGroup { Children = { new SkewTransform(-20, 0), _sheenMove } },
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false,
                Opacity = 0,
            };
            layers.Children.Add(_sheen);
            _glow = new DropShadowEffect { Color = Theme.EmberColor, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.34 };
            Effect = _glow;
        }
        layers.Children.Add(_hover);
        layers.Children.Add(_row);
        _root.Child = layers;

        var host = new Grid();
        host.Children.Add(_root);
        host.Children.Add(_ring);
        Content = host;

        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.45;
        Loaded += (_, _) => StartSheen();
        Unloaded += (_, _) => StopSheen();
        SizeChanged += (_, _) => { if (IsLoaded) StartSheen(); };
    }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = _kind == ButtonKind.Primary ? value.ToUpperInvariant() : value;
    }

    private string? _iconData;
    private readonly bool _iconOnly;

    public void SetIcon(string? data)
    {
        _iconData = data;
        if (_icon is not null) _row.Children.Remove(_icon);
        _icon = null;
        if (data is null) return;
        var color = _kind switch { ButtonKind.Primary => Brushes.White, ButtonKind.Danger => Theme.Err, _ => Theme.InkDim };
        _icon = Icons.Make(data, color, _kind == ButtonKind.Primary ? 20 : 17, 1.8, filled: data == Icons.Play);
        _icon.Margin = new Thickness(0, 0, _iconOnly ? 0 : 9, 0);
        _icon.At(0, 0);
        _row.Children.Add(_icon);
    }

    /// <summary>Occupé : l'icône devient un cercle qui tourne et le bouton ne réagit plus.</summary>
    public bool IsBusy
    {
        get => _busy;
        set
        {
            if (_busy == value) return;
            _busy = value;
            IsHitTestVisible = !value;
            Anim.StopClock(_spinClock);
            _spinClock = null;
            if (value)
            {
                if (_icon is not null) _row.Children.Remove(_icon);
                var spinner = Icons.Make(Icons.Refresh, _kind == ButtonKind.Primary ? Brushes.White : Theme.InkDim, 18, 1.8);
                spinner.Margin = new Thickness(0, 0, 9, 0);
                spinner.At(0, 0);
                _icon = spinner;
                _row.Children.Add(spinner);
                _spinClock = Anim.Spin(spinner);
            }
            else
            {
                SetIcon(_iconData);
            }
        }
    }

    // ------------------------------------------------------------------ interactions

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (!IsEnabled) return;
        Anim.Fade(_hover, _kind == ButtonKind.Primary ? 0.14 : 0.07, 140);
        if (_glow is not null) Anim.To(_glow, DropShadowEffect.OpacityProperty, 0.68, 200);
        if (_kind == ButtonKind.Secondary) _root.BorderBrush = Theme.LineHi;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Anim.Fade(_hover, 0, 200);
        if (_glow is not null) Anim.To(_glow, DropShadowEffect.OpacityProperty, 0.34, 260);
        if (_kind == ButtonKind.Secondary) _root.BorderBrush = Theme.Line;
        Press(false);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Press(true);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        Press(false);
    }

    private void Press(bool down)
    {
        var to = down ? 0.972 : 1.0;
        Anim.To(_scale, ScaleTransform.ScaleXProperty, to, down ? 80 : 220, 0, down ? Anim.Out : Anim.Back);
        Anim.To(_scale, ScaleTransform.ScaleYProperty, to, down ? 80 : 220, 0, down ? Anim.Out : Anim.Back);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        // Anneau de focus seulement à la navigation clavier (jamais après un simple clic).
        if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) Anim.Fade(_ring, 1, 120);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Anim.Fade(_ring, 0, 120);
    }

    // ------------------------------------------------------------------ reflet

    private void StartSheen()
    {
        StopSheen();
        if (_sheen is null || !Anim.Enabled || ActualWidth < 40) return;
        _sheen.Opacity = 1;
        var width = ActualWidth;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromSeconds(5.2)) };
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(-90, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(width + 40, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.15)), Anim.InOut));
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(width + 40, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5.2))));
        _sheenClock = Anim.Loop(_sheenMove, TranslateTransform.XProperty, frames);
    }

    private void StopSheen()
    {
        Anim.StopLoop(_sheenMove, TranslateTransform.XProperty, _sheenClock);
        _sheenClock = null;
    }
}
