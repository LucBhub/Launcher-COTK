using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace COTK.Next.UI;

/// <summary>Petits constructeurs d'interface : l'UI est écrite en C# (pas de XAML) pour compiler
/// partout, y compris sans SDK. Ces helpers gardent le code des vues lisible.</summary>
internal static class Ui
{
    public static TextBlock Text(string text, double size = 13, Brush? color = null, FontWeight? weight = null,
        FontFamily? font = null, bool wrap = false, TextAlignment align = TextAlignment.Left)
        => new LocalizedText()
        {
            Text = text,
            FontSize = size,
            Foreground = color ?? Theme.Ink,
            FontWeight = weight ?? FontWeights.Normal,
            FontFamily = font ?? Theme.Body,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            TextAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };

    public static TextBlock Raw(string text, double size=13, Brush? color=null, FontWeight? weight=null, FontFamily? font=null, bool wrap=false)
        => new LocalizedText { Translate=false, Text=text, FontSize=size, Foreground=color??Theme.Ink,
            FontWeight=weight??FontWeights.Normal, FontFamily=font??Theme.Body, TextTrimming=wrap?TextTrimming.None:TextTrimming.CharacterEllipsis,
            TextWrapping=wrap?TextWrapping.Wrap:TextWrapping.NoWrap,
            VerticalAlignment=VerticalAlignment.Center };

    /// <summary>Police d'affichage (Oswald) : titres, gros chiffres, boutons.</summary>
    public static TextBlock Display(string text, double size, Brush? color = null, FontWeight? weight = null)
        => Text(text, size, color, weight ?? FontWeights.SemiBold, Theme.Display);

    public static TextBlock Mono(string text, double size = 11, Brush? color = null)
        => Text(text, size, color ?? Theme.InkMute, FontWeights.Normal, Theme.Mono);

    /// <summary>Capitales aérées (WPF n'a pas d'espacement de lettres) : intercale des fines espaces.</summary>
    public static string Spaced(string text)
        => string.Join('\u200A', text.ToUpperInvariant().Select(c => c.ToString()));

    public static T M<T>(this T element, double l, double t, double r, double b) where T : FrameworkElement
    {
        element.Margin = new Thickness(l, t, r, b);
        return element;
    }

    public static T M<T>(this T element, double all) where T : FrameworkElement
    {
        element.Margin = new Thickness(all);
        return element;
    }

    public static T At<T>(this T element, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : UIElement
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, columnSpan);
        return element;
    }

    public static T Align<T>(this T element, HorizontalAlignment h, VerticalAlignment v = VerticalAlignment.Center) where T : FrameworkElement
    {
        element.HorizontalAlignment = h;
        element.VerticalAlignment = v;
        return element;
    }

    public static StackPanel VStack(params UIElement[] children) => Stack(Orientation.Vertical, children);

    public static StackPanel HStack(params UIElement[] children) => Stack(Orientation.Horizontal, children);

    private static StackPanel Stack(Orientation orientation, UIElement[] children)
    {
        var panel = new StackPanel { Orientation = orientation };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static Grid Columns(params GridLength[] widths)
    {
        var grid = new Grid();
        foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        return grid;
    }

    public static Grid Rows(params GridLength[] heights)
    {
        var grid = new Grid();
        foreach (var height in heights) grid.RowDefinitions.Add(new RowDefinition { Height = height });
        return grid;
    }

    public static readonly GridLength Auto = GridLength.Auto;

    public static GridLength Star(double weight = 1) => new(weight, GridUnitType.Star);

    public static GridLength Px(double pixels) => new(pixels);

    public static Border Rule(Brush? color = null, double thickness = 1) => new()
    {
        Height = thickness,
        Background = color ?? Theme.Line,
        SnapsToDevicePixels = true,
        IsHitTestVisible = false,
    };

    public static Border VRule(Brush? color = null) => new()
    {
        Width = 1,
        Background = color ?? Theme.Line,
        SnapsToDevicePixels = true,
        IsHitTestVisible = false,
    };

    public static Border Dot(Brush color, double size = 8) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = color,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
