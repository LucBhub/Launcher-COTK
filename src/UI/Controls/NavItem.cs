using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace COTK.Next.UI;

/// <summary>Entrée de la barre latérale : icône + libellé, survol doux, état sélectionné teinté braise.
/// Vrai Button : navigation au clavier (Tab, Entrée, Espace) incluse.</summary>
internal sealed class NavItem : Button
{
    private readonly Icon _icon;
    private readonly TextBlock _label;
    private readonly Border _bg;
    private readonly SolidColorBrush _fill = new(Colors.Transparent);
    private readonly Border _ring;
    private bool _selected;
    private bool _hover;

    public Page Target { get; }

    public NavItem(Page target, string text, string icon)
    {
        Target = target;
        Template = FxButton.Bare;
        Height = 40;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        UseLayoutRounding = true;

        _icon = Icons.Make(icon, Theme.InkMute, 19, 1.7);
        _label = Ui.Text(text, 14, Theme.InkDim, FontWeights.Medium);
        var row = Ui.Columns(Ui.Px(46), Ui.Star());
        _icon.At(0, 0).Align(HorizontalAlignment.Center);
        _label.At(0, 1);
        row.Children.Add(_icon);
        row.Children.Add(_label);

        _bg = new Border { CornerRadius = new CornerRadius(10), Background = _fill, Child = row };
        _ring = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), BorderBrush = Theme.WithAlpha(Theme.GoldColor, 220), Opacity = 0, IsHitTestVisible = false };
        var host = new Grid();
        host.Children.Add(_bg);
        host.Children.Add(_ring);
        Content = host;
        AutomationProperties_SetName(text);
    }

    private void AutomationProperties_SetName(string text) => System.Windows.Automation.AutomationProperties.SetName(this, text);

    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            Apply();
        }
    }

    private void Apply()
    {
        Anim.ColorTo(_fill, _selected ? Color.FromArgb(24, 255, 255, 255) : _hover ? Color.FromArgb(12, 255, 255, 255) : Colors.Transparent, 140);
        _label.Foreground = _selected ? Theme.Ink : _hover ? Theme.Ink : Theme.InkDim;
        _label.FontWeight = _selected ? FontWeights.SemiBold : FontWeights.Medium;
        _icon.Color = _selected ? Theme.EmberHi : _hover ? Theme.InkDim : Theme.InkMute;
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); _hover = true; Apply(); }

    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); _hover = false; Apply(); }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) Anim.Fade(_ring, 1, 100);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Anim.Fade(_ring, 0, 100);
    }
}
