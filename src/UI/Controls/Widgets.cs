using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shapes = System.Windows.Shapes;

namespace COTK.Next.UI;

/// <summary>Carte : fond dégradé, bordure fine, coins arrondis.</summary>
internal class Card : Border
{
    public Card(double radius = 14)
    {
        CornerRadius = new CornerRadius(radius);
        Background = Theme.CardGradient;
        BorderBrush = Theme.Line;
        BorderThickness = new Thickness(1);
        SnapsToDevicePixels = true;
    }
}

/// <summary>Barre de progression dessinée à la main : remplissage braise avec tête lumineuse et reflet
/// qui glisse, mode indéterminé, valeur animée en douceur. Une seule surface, pas d'arbre visuel.</summary>
internal sealed class ProgressBarFx : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(ProgressBarFx),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(ProgressBarFx),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Track = Theme.White08;
    private static readonly Brush Shine = Theme.Gradient(0, 0, 1, 0,
        (0, Color.FromArgb(0, 255, 255, 255)), (0.5, Color.FromArgb(120, 255, 255, 255)), (1, Color.FromArgb(0, 255, 255, 255)));
    private static readonly Brush Head = Theme.Radial(Color.FromArgb(210, 255, 170, 90), Color.FromArgb(0, 255, 106, 43));

    private AnimationClock? _phaseClock;
    private bool _indeterminate;

    public ProgressBarFx()
    {
        Height = 8;
        IsHitTestVisible = false;
        Loaded += (_, _) => StartPhase();
        Unloaded += (_, _) => { Anim.StopClock(_phaseClock); _phaseClock = null; };
    }

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public bool Indeterminate
    {
        get => _indeterminate;
        set { _indeterminate = value; InvalidateVisual(); }
    }

    /// <summary>Se rapproche de la valeur par une courte animation (appelé ~7×/s pendant un transfert).</summary>
    public void SetFraction(double value, int ms = 260) => Anim.To(this, FractionProperty, Math.Clamp(value, 0, 1), ms, 0, Anim.Out);

    public void Reset() { BeginAnimation(FractionProperty, null); Fraction = 0; }

    private void StartPhase()
    {
        if (_phaseClock is not null) return;
        _phaseClock = Anim.Loop(this, PhaseProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(1.7))));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var r = h / 2;
        var bounds = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(Track, null, bounds, r, r);

        if (_indeterminate)
        {
            var seg = w * 0.32;
            var x = -seg + Phase * (w + seg);
            dc.PushClip(new RectangleGeometry(bounds, r, r));
            dc.DrawRoundedRectangle(Theme.EmberGradientH, null, new Rect(x, 0, seg, h), r, r);
            dc.Pop();
            return;
        }

        var fraction = Math.Clamp(Fraction, 0, 1);
        if (fraction <= 0.0001) return;
        var fillWidth = Math.Max(h, w * fraction);
        var fill = new Rect(0, 0, fillWidth, h);
        dc.DrawRoundedRectangle(Theme.EmberGradientH, null, fill, r, r);

        dc.PushClip(new RectangleGeometry(fill, r, r));
        var shineWidth = Math.Max(60, fillWidth * 0.3);
        dc.DrawRectangle(Shine, null, new Rect(-shineWidth + Phase * (fillWidth + shineWidth), 0, shineWidth, h));
        dc.Pop();

        // tête lumineuse à l'extrémité du remplissage
        dc.DrawEllipse(Head, null, new Point(fillWidth - r, r), h * 2.4, h * 2.4);
    }
}

/// <summary>Écusson de rang vectoriel : n chevrons (1 à 5) ou couronne (Royauté), teinté par palier.
/// Atteint = couleur pleine ; à venir = estompé ; courant = halo qui respire.</summary>
internal sealed class TierBadge : Grid
{
    public TierBadge(int tier, double size = 56, bool achieved = true, bool current = false, LoopGroup? group = null)
    {
        Width = size;
        Height = size * 1.12;
        IsHitTestVisible = false;
        var color = Theme.TierColor(tier);
        var lit = achieved || current;

        var canvas = new Canvas { Width = 64, Height = 72 };
        if (current)
        {
            var halo = new Shapes.Ellipse { Width = 120, Height = 120, Fill = Theme.Radial(Color.FromArgb(110, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)) };
            Canvas.SetLeft(halo, -28);
            Canvas.SetTop(halo, -24);
            canvas.Children.Add(halo);
            Anim.Pulse(halo, 0.45, 1, 2200, group);
        }

        if(ClientRankAssets.Load(tier) is { } original)
        {
            var image=new Image { Source=original, Width=64, Height=64, Stretch=Stretch.Uniform, Opacity=lit ? 1 : 0.32 };
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
            Canvas.SetTop(image,4); canvas.Children.Add(image);
            Children.Add(new Viewbox { Child=canvas, Stretch=Stretch.Uniform });
            return;
        }
        var lightC = Lerp(color, Colors.White, 0.28);
        var darkC = Lerp(color, Colors.Black, 0.55);
        var fill = lit
            ? Theme.Gradient(0, 0, 0, 1, (0, Color.FromArgb(70, lightC.R, lightC.G, lightC.B)), (1, Color.FromArgb(34, darkC.R, darkC.G, darkC.B)))
            : Theme.Gradient(0, 0, 0, 1, (0, Theme.C("#1d1d23")), (1, Theme.C("#141418")));
        var edge = lit ? Theme.WithAlpha(color, 255) : Theme.WithAlpha(Theme.C("#34343d"), 255);

        canvas.Children.Add(new Shapes.Path
        {
            Data = Icons.Geo("M32 3 58 12v23c0 17-11 28-26 34C17 63 6 52 6 35V12z"),
            Fill = fill, Stroke = edge, StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round,
        });
        canvas.Children.Add(new Shapes.Path
        {
            Data = Icons.Geo("M32 10.5 51 17.5v17.5c0 12.5-8.5 21.5-19 26C21.500 56.500 13 47.500 13 35V17.500z"),
            Stroke = Theme.WithAlpha(lit ? color : Theme.C("#2a2a32"), lit ? (byte)90 : (byte)255), StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round,
        });

        var glyph = lit ? Theme.WithAlpha(lightC, 255) : Theme.WithAlpha(Theme.C("#3a3a44"), 255);
        if (tier >= 6)
        {
            canvas.Children.Add(new Shapes.Path
            {
                Data = Icons.Geo("M17 44 20 27l9 9 3-14 3 14 9-9 3 17z"),
                Fill = glyph, Stroke = glyph, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round,
            });
        }
        else if (tier >= 1)
        {
            // n chevrons empilés, centrés verticalement
            var gap = 8.0;
            var top = 36 - (tier - 1) * gap / 2 - 4;
            for (var i = 0; i < tier; i++)
            {
                var y = top + i * gap;
                canvas.Children.Add(new Shapes.Path
                {
                    Data = Icons.Geo($"M21 {y + 7:0.#}l11-8 11 8"),
                    Stroke = glyph, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                });
            }
        }
        else
        {
            canvas.Children.Add(new Shapes.Path { Data = Icons.Geo("M24 37h16"), Stroke = glyph, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }

        Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}

/// <summary>Champ de saisie : icône, texte d'aide, anneau braise au focus, état d'erreur, bouton « voir »
/// pour le mot de passe. Enveloppe un vrai TextBox / PasswordBox (copier-coller, accessibilité, IME).</summary>
internal sealed class FxInput : Border
{
    private readonly TextBox _text = new();
    private readonly PasswordBox _password = new();
    private readonly TextBlock _placeholder;
    private readonly bool _isPassword;
    private readonly SolidColorBrush _border = new(Theme.LineColor);
    private bool _revealed;
    private bool _error;

    public event Action? Submitted;

    public FxInput(string placeholder, string icon, bool password = false)
    {
        _isPassword = password;
        Height = 46;
        CornerRadius = new CornerRadius(11);
        Background = Theme.WithAlpha(Colors.Black, 90);
        BorderBrush = _border;
        BorderThickness = new Thickness(1);
        SnapsToDevicePixels = true;

        foreach (Control box in new Control[] { _text, _password })
        {
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.Foreground = Theme.Ink;
            box.FontFamily = Theme.Body;
            box.FontSize = 14;
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Padding = new Thickness(0);
            box.FocusVisualStyle = null;
            box.Margin = new Thickness(0, 0, password ? 4 : 14, 0);
        }
        _text.CaretBrush = Brushes.White;
        _text.SelectionBrush = Theme.WithAlpha(Theme.EmberColor, 140);
        _password.CaretBrush = Brushes.White;
        _password.SelectionBrush = Theme.WithAlpha(Theme.EmberColor, 140);
        _password.PasswordChar = '•';
        _text.Visibility = password ? Visibility.Collapsed : Visibility.Visible;
        _password.Visibility = password ? Visibility.Visible : Visibility.Collapsed;

        _placeholder = Ui.Text(placeholder, 14, Theme.InkMute);
        _placeholder.IsHitTestVisible = false;

        var cell = new Grid();
        cell.Children.Add(_placeholder);
        cell.Children.Add(_text);
        cell.Children.Add(_password);

        var row = Ui.Columns(Ui.Px(44), Ui.Star(), Ui.Auto);
        var lead = Icons.Make(icon, Theme.InkMute, 18, 1.7);
        lead.At(0, 0).Align(HorizontalAlignment.Center);
        cell.At(0, 1);
        row.Children.Add(lead);
        row.Children.Add(cell);

        if (password)
        {
            var eye = new Button
            {
                Width = 40, Height = 46, Cursor = Cursors.Hand, Focusable = false, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Content = Icons.Make(Icons.Eye, Theme.InkMute, 17), ToolTip = Ui.Text("Afficher / masquer",12),
            };
            eye.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='Transparent'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
            eye.Click += (_, _) => ToggleReveal();
            eye.At(0, 2);
            row.Children.Add(eye);
        }

        Child = row;

        _text.TextChanged += (_, _) => { Sync(); ClearError(); };
        _password.PasswordChanged += (_, _) => { Sync(); ClearError(); };
        GotKeyboardFocus += (_, _) => Anim.ColorTo(_border, _error ? Theme.ErrColor : Theme.EmberColor, 160);
        LostKeyboardFocus += (_, _) => Anim.ColorTo(_border, _error ? Theme.ErrColor : Theme.LineColor, 220);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Submitted?.Invoke();
        };
        MouseLeftButtonDown += (_, _) => FocusInput();
    }

    public string Value
    {
        get => _isPassword && !_revealed ? _password.Password : _text.Text;
        set
        {
            if (_isPassword) _password.Password = value;
            _text.Text = value;
        }
    }

    public void FocusInput() => (_isPassword && !_revealed ? (Control)_password : _text).Focus();

    public void SetError(bool error)
    {
        _error = error;
        Anim.ColorTo(_border, error ? Theme.ErrColor : (IsKeyboardFocusWithin ? Theme.EmberColor : Theme.LineColor), 160);
    }

    public bool InputEnabled
    {
        get => _text.IsEnabled;
        set { _text.IsEnabled = value; _password.IsEnabled = value; Opacity = value ? 1 : 0.6; }
    }

    private void ClearError()
    {
        if (_error) SetError(false);
    }

    private void Sync() => _placeholder.Visibility = Value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ToggleReveal()
    {
        _revealed = !_revealed;
        if (_revealed)
        {
            _text.Text = _password.Password;
            _text.Visibility = Visibility.Visible;
            _password.Visibility = Visibility.Collapsed;
            _text.Focus();
            _text.CaretIndex = _text.Text.Length;
        }
        else
        {
            _password.Password = _text.Text;
            _text.Text = string.Empty;
            _text.Visibility = Visibility.Collapsed;
            _password.Visibility = Visibility.Visible;
            _password.Focus();
        }
    }
}

/// <summary>Lignes grises qui scintillent pendant le chargement (au lieu d'un écran vide).</summary>
internal sealed class Skeleton : Border
{
    public Skeleton(double width, double height, double radius = 6)
    {
        Width = double.IsNaN(width) || width <= 0 ? double.NaN : width;
        Height = height;
        CornerRadius = new CornerRadius(radius);
        Background = Theme.White08;
        IsHitTestVisible = false;
        Loaded += (_, _) => Anim.Pulse(this, 0.45, 1, 1100);
    }
}
