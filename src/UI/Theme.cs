using System.Windows;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Identité visuelle : noir chaud, braise (orange) et or. Toutes les brosses fixes sont gelées
/// (Freeze) : partagées entre contrôles sans coût de suivi de changements.</summary>
internal static class Theme
{
    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ---- couleurs ----
    public static readonly Color BgColor = C("#09090b");
    public static readonly Color PanelColor = C("#0d0d10");
    public static readonly Color CardColor = C("#131317");
    public static readonly Color CardHiColor = C("#1a1a20");
    public static readonly Color LineColor = C("#25252c");
    public static readonly Color LineHiColor = C("#3a3a44");
    public static readonly Color InkColor = C("#f5f5f7");
    public static readonly Color InkDimColor = C("#a3a3b0");
    public static readonly Color InkMuteColor = C("#6c6c79");
    public static readonly Color EmberColor = C("#ff6a2b");
    public static readonly Color EmberHiColor = C("#ff9a45");
    public static readonly Color EmberLoColor = C("#d4431a");
    public static readonly Color GoldColor = C("#ffc247");
    public static readonly Color OkColor = C("#4fd18b");
    public static readonly Color ErrColor = C("#ff5d63");
    public static readonly Color InfoColor = C("#5aa9ff");

    // ---- brosses ----
    public static readonly Brush Bg = Solid(BgColor);
    public static readonly Brush Panel = Solid(PanelColor);
    public static readonly Brush Card = Solid(CardColor);
    public static readonly Brush CardHi = Solid(CardHiColor);
    public static readonly Brush Line = Solid(LineColor);
    public static readonly Brush LineHi = Solid(LineHiColor);
    public static readonly Brush Ink = Solid(InkColor);
    public static readonly Brush InkDim = Solid(InkDimColor);
    public static readonly Brush InkMute = Solid(InkMuteColor);
    public static readonly Brush Ember = Solid(EmberColor);
    public static readonly Brush EmberHi = Solid(EmberHiColor);
    public static readonly Brush Gold = Solid(GoldColor);
    public static readonly Brush Ok = Solid(OkColor);
    public static readonly Brush Err = Solid(ErrColor);
    public static readonly Brush Info = Solid(InfoColor);
    public static readonly Brush Transparent = Brushes.Transparent;
    public static readonly Brush White04 = Solid(Color.FromArgb(10, 255, 255, 255));
    public static readonly Brush White08 = Solid(Color.FromArgb(20, 255, 255, 255));
    public static readonly Brush White12 = Solid(Color.FromArgb(31, 255, 255, 255));
    public static readonly Brush Black40 = Solid(Color.FromArgb(102, 0, 0, 0));
    public static readonly Brush Scrim = Solid(Color.FromArgb(178, 4, 4, 6));

    public static readonly Brush EmberGradient = Gradient(0, 0, 1, 1, (0, EmberHiColor), (0.55, EmberColor), (1, EmberLoColor));
    public static readonly Brush EmberGradientH = Gradient(0, 0.5, 1, 0.5, (0, EmberHiColor), (1, EmberLoColor));
    public static readonly Brush CardGradient = Gradient(0, 0, 0, 1, (0, C("#18181d")), (1, C("#111115")));
    public static readonly Brush SidebarGradient = Gradient(0, 0, 0, 1, (0, C("#0f0f13")), (1, C("#0a0a0d")));

    public static LinearGradientBrush Gradient(double x0, double y0, double x1, double y1, params (double offset, Color color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(x0, y0), EndPoint = new Point(x1, y1) };
        foreach (var (offset, color) in stops) brush.GradientStops.Add(new GradientStop(color, offset));
        brush.Freeze();
        return brush;
    }

    public static RadialGradientBrush Radial(Color center, Color edge, double cx = 0.5, double cy = 0.5, double radius = 0.5)
    {
        var brush = new RadialGradientBrush(center, edge)
        {
            GradientOrigin = new Point(cx, cy),
            Center = new Point(cx, cy),
            RadiusX = radius,
            RadiusY = radius,
        };
        brush.Freeze();
        return brush;
    }

    public static Brush WithAlpha(Color color, byte alpha) => Solid(Color.FromArgb(alpha, color.R, color.G, color.B));

    // ---- paliers de rang ----
    public static Color TierColor(int tier) => tier switch
    {
        1 => C("#d58a52"),   // Bronze
        2 => C("#c4cbd6"),   // Argent
        3 => C("#ffc247"),   // Or
        4 => C("#3fd6c6"),   // Platine
        5 => C("#6db8ff"),   // Diamant
        6 => C("#d27dff"),   // Royauté
        _ => C("#6c6c79"),   // Non classé
    };

    // ---- typographie ----
    public static readonly FontFamily Body = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    public static readonly FontFamily Display = LoadDisplay();

    private static FontFamily LoadDisplay()
    {
        try
        {
            var dir = AppPaths.FontsDir;
            if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.ttf").Length > 0)
            {
                var uri = new Uri("file:///" + dir.Replace('\\', '/').TrimEnd('/') + "/");
                return new FontFamily(uri, "./#Oswald, Bahnschrift, Segoe UI");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("police d'affichage non chargée", ex);
        }
        return new FontFamily("Bahnschrift, Segoe UI");
    }
}
