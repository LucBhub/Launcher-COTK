using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace COTK.Next.UI;

/// <summary>Jeu d'icônes vectorielles maison (grille 24×24, trait arrondi). Aucune image, aucun
/// fichier : net à toutes les échelles DPI et teintable par code.</summary>
internal static class Icons
{
    public const string Home = "M3.5 11 12 4l8.5 7M5.5 9.7V20h4.5v-5.5h4V20h4.5V9.7";
    public const string Library = "M4 4h6.5v6.5H4zM13.5 4H20v6.5h-6.5zM4 13.5h6.5V20H4zM13.5 13.5H20V20h-6.5z";
    public const string Matches = "M12 3v4M12 17v4M3 12h4M17 12h4M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0z";
    public const string Leaderboard = "M5 20v-7M12 20V4M19 20V9M3 20h18";
    public const string Settings = "M4 7h9M17 7h3M4 12h3M11 12h9M4 17h11M19 17h1M15 7a2 2 0 1 0 4 0 2 2 0 0 0-4 0zM7 12a2 2 0 1 0 4 0 2 2 0 0 0-4 0zM13 17a2 2 0 1 0 4 0 2 2 0 0 0-4 0z";
    public const string Crown = "M3 18 5 7l5 5 2-8 2 8 5-5 2 11zM4.5 21h15";
    public const string Close = "M6 6l12 12M18 6 6 18";
    public const string Minimize = "M6 12h12";
    public const string Check = "M5 12.5l4.5 4.5L19 7.5";
    public const string Play = "M8 5.5v13l11-6.5z";
    public const string Download = "M12 4v11M7.5 10.5 12 15l4.5-4.5M5 20h14";
    public const string Refresh = "M20 5v5h-5M4 19v-5h5M5.6 9a7.5 7.5 0 0 1 13.2.8M18.4 15a7.5 7.5 0 0 1-13.2-.8";
    public const string Folder = "M3 7.5h6.5l2 2H21V19H3z";
    public const string Copy = "M9 9h11v11H9zM5 15V4h11";
    public const string Warning = "M12 3.5 21.5 20h-19zM12 10v5M12 17.6v.4";
    public const string Globe = "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0zM3 12h18M12 3c-3.5 3.8-3.5 14.2 0 18M12 3c3.5 3.8 3.5 14.2 0 18";
    public const string Shield = "M12 3 20 6v6c0 4.5-3.2 7.8-8 9-4.8-1.2-8-4.5-8-9V6z";
    public const string External = "M14 4h6v6M20 4l-9 9M18 14v6H4V6h6";
    public const string User = "M16 8a4 4 0 1 1-8 0 4 4 0 0 1 8 0zM4 21c0-4.4 3.6-7 8-7s8 2.6 8 7";
    public const string Logout = "M9 4H4v16h5M16 8l5 4-5 4M21 12H9";
    public const string Key = "M15.5 8.5a4 4 0 1 1-1 2.7L4 21v-3h3v-3h3l2.3-2.3";
    public const string Bolt = "M13 3 5 13.5h6L10 21l8-10.5h-6z";
    public const string Trophy = "M7 4h10v5a5 5 0 0 1-10 0zM7 6H4v2a3 3 0 0 0 3 3M17 6h3v2a3 3 0 0 1-3 3M12 14v4M8.5 20h7";
    public const string Skull = "M12 3a7 7 0 0 0-4.5 12.3V18h9v-2.7A7 7 0 0 0 12 3zM9.5 18v2M12 18v2M14.5 18v2M9.5 10.5h.01M14.5 10.5h.01";
    public const string Lock = "M6 11h12v9H6zM8.5 11V8a3.5 3.5 0 0 1 7 0v3";
    public const string Info = "M12 21a9 9 0 1 1 0-18 9 9 0 0 1 0 18zM12 11v5M12 8v.4";
    public const string Bell = "M6 17V11a6 6 0 0 1 12 0v6l1.5 2h-15zM10 21h4";
    public const string Pause = "M8 5v14M16 5v14";
    public const string Wrench = "M14.5 6.5a4 4 0 0 0 5 5L10 21l-3-3 9.5-9.5";
    public const string Eye = "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0z";
    public const string Search = "M17 10.5a6.5 6.5 0 1 1-13 0 6.5 6.5 0 0 1 13 0zM16 16l5 5";

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry Geo(string data)
    {
        if (Cache.TryGetValue(data, out var geo)) return geo;
        geo = Geometry.Parse(data);
        geo.Freeze();
        Cache[data] = geo;
        return geo;
    }

    public static Icon Make(string data, Brush? color = null, double size = 18, double thickness = 1.7, bool filled = false)
        => new(data, color ?? Theme.InkDim, size, thickness, filled);
}

/// <summary>Une icône teintable (la couleur peut être animée ou changée au survol).</summary>
internal sealed class Icon : Viewbox
{
    private readonly Shapes.Path _path;

    public Icon(string data, Brush color, double size, double thickness, bool filled)
    {
        Width = size;
        Height = size;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        _path = new Shapes.Path
        {
            Data = Icons.Geo(data),
            Stroke = color,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = filled ? color : null,
            SnapsToDevicePixels = false,
        };
        Child = new Canvas { Width = 24, Height = 24, Children = { _path } };
    }

    public Brush Color
    {
        get => _path.Stroke;
        set
        {
            _path.Stroke = value;
            if (_path.Fill is not null) _path.Fill = value;
        }
    }
}
