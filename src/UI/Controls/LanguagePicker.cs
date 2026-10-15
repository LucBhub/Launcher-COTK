using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;
using COTK.Next.Core;

namespace COTK.Next.UI;

internal sealed class LanguagePicker : Grid
{
    private readonly FxButton _button = new("EN ▾", ButtonKind.Secondary, null, 32, 12) { Width=86 };
    private readonly Popup _popup = new() { StaysOpen=false, AllowsTransparency=true, Placement=PlacementMode.Bottom };

    public LanguagePicker()
    {
        Children.Add(_button);
        WindowChrome.SetIsHitTestVisibleInChrome(_button,true);
        var items=new StackPanel();
        foreach (var (code,name) in new[] { ("en","English"),("fr","Français"),("zh-CN","简体中文") })
        {
            var choose = new FxButton(name,ButtonKind.Ghost,null,38,13);
            choose.Click += (_,_) => { _popup.IsOpen=false; L.Select(code); };
            items.Children.Add(choose);
        }
        _popup.Child=new Card(12) { Width=174, Padding=new Thickness(6), Child=items, Background=Theme.CardHi, BorderBrush=Theme.LineHi };
        _popup.PlacementTarget=_button;
        _button.Click += (_,_) => _popup.IsOpen=!_popup.IsOpen;
        Loaded += (_,_) => { L.Changed += Refresh; Refresh(); };
        Unloaded += (_,_) => { L.Changed -= Refresh; _popup.IsOpen=false; };
    }

    private void Refresh()
    {
        _button.Text=(L.Language=="fr" ? "FR" : L.Language=="zh-CN" ? "中文" : "EN") + " ▾";
        System.Windows.Automation.AutomationProperties.SetName(_button,L.T("Langue"));
    }
}
