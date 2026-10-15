using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Retains the source value in the dependency property: language changes re-coerce it in place.
/// No window restart, no recreation of the 3D scene and no interruption to downloads or login polling.</summary>
internal sealed class LocalizedText : TextBlock
{
    public bool Translate { get; set; } = true;
    private static readonly FontFamily Chinese = new("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

    static LocalizedText()
    {
        TextProperty.OverrideMetadata(typeof(LocalizedText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure,
            (obj,_) => obj.CoerceValue(FontFamilyProperty),
            (obj,value) => ((LocalizedText)obj).Translate ? L.T((string)value) : value));
        FontFamilyProperty.OverrideMetadata(typeof(LocalizedText), new FrameworkPropertyMetadata(Theme.Body, FrameworkPropertyMetadataOptions.AffectsMeasure, null,
            (obj,value) => ((LocalizedText)obj).Text.Any(c=>c is >= '\u3400' and <= '\u9fff') ? Chinese : value));
    }

    public LocalizedText()
    {
        Loaded += (_,_) => { L.Changed += Refresh; Refresh(); };
        Unloaded += (_,_) => L.Changed -= Refresh;
    }

    private void Refresh()
    {
        CoerceValue(TextProperty);
        CoerceValue(FontFamilyProperty);
    }
}
