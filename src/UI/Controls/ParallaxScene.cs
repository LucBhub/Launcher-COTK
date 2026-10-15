using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shapes = System.Windows.Shapes;

namespace COTK.Next.UI;

/// <summary>Décor animé d'un écran : visuel clé du projet, ou paysage vectoriel de repli.</summary>
internal interface IScene
{
    FrameworkElement Element { get; }
    LoopGroup Group { get; }
    void Pointer(double nx, double ny);
}

internal static class SceneFactory
{
    public static IScene Create(string art, double width, double height, double sunX, double sunY)
    {
        return new ParallaxScene(width, height);
    }
}

/// <summary>Décor : crépuscule sur une chaîne de montagnes, forêt de pins, parachutes qui dérivent.
/// 100 % vectoriel et généré par code (graine fixe = toujours le même tableau) : aucune image à embarquer,
/// net à tous les DPI. Chaque couche est mise en cache bitmap puis simplement déplacée — le GPU compose,
/// le CPU ne redessine rien. Parallaxe douce vers la souris + dérive lente autonome.
/// La scène s'adapte à n'importe quelle taille (bannière 848×300, écran de connexion plein cadre).</summary>
internal sealed class ParallaxScene : Grid, IFrameUpdatable, IScene
{
    private sealed class Layer
    {
        public required TranslateTransform Pointer;
        public required double Depth;
    }

    private readonly double _w, _h, _sx, _sy, _bleed;
    private readonly List<Layer> _layers = new();
    private double _targetX, _targetY, _x, _y;
    private bool _settled = true;
    private readonly Forest3D _forest;

    /// <summary>Toutes les boucles de la scène : on les endort quand la vue n'est pas à l'écran.</summary>
    public LoopGroup Group { get; } = new();

    public FrameworkElement Element => this;

    public ParallaxScene(double width = 848, double height = 300)
    {
        _w = width;
        _h = height;
        _sx = width / 848.0;
        _sy = height / 300.0;
        _bleed = 140 * _sx;
        ClipToBounds = true;
        Width = width;
        Height = height;
        Background = Theme.Gradient(0, 0, 0, 1,
            (0, Theme.C("#0b0a12")), (0.40, Theme.C("#26121d")), (0.72, Theme.C("#7b2f22")), (1, Theme.C("#e5712f")));

        var canvasZoom = new ScaleTransform(1, 1, 560 * (width / 848.0), 214 * (height / 300.0));
        var canvas = new Canvas { Width = width, Height = height, IsHitTestVisible = false, RenderTransform = canvasZoom };
        Children.Add(canvas);
        _forest = new Forest3D(width, height);
        Children.Add(_forest);
        AddFilmLook();

        AddStars(canvas);
        AddSun(canvas);
        AddRays(canvas);
        AddClouds(canvas);
        AddLayer(canvas, Ridge(11, -_bleed, _w + _bleed, 196 * _sy, 54 * _sy, 0.58, _h + 20), FarFill, 0.010, 9, 46);
        AddMist(canvas, 150 * _sy, 0.16);
        AddLayer(canvas, Ridge(23, -_bleed, _w + _bleed, 218 * _sy, 46 * _sy, 0.56, _h + 20), MidFill, 0.022, 12, 38);
        AddMist(canvas, 190 * _sy, 0.12);
        AddLayer(canvas, Ridge(37, -_bleed, _w + _bleed, 244 * _sy, 30 * _sy, 0.52, _h + 20), NearFill, 0.040, 14, 31);
        // premier plan : vraie forêt 3D (Forest3D), ajoutée au-dessus du décor


        AddParachutes(canvas);

        Loaded += (_, _) => Ticker.Add(this);
        Unloaded += (_, _) => Ticker.Remove(this);
    }

    public bool IsActive => IsVisible && Group.Active;

    // ------------------------------------------------------------------ pointeur

    /// <summary>Position de la souris normalisée (-1…1) ; (0,0) au repos.</summary>
    public void Pointer(double nx, double ny)
    {
        _targetX = Math.Clamp(nx, -1, 1);
        _targetY = Math.Clamp(ny, -1, 1);
        _settled = false;
    }

    public void Update(double dt)
    {
        var k = 1 - Math.Exp(-dt * 4.5); // lissage exponentiel, indépendant de la cadence
        var dx = _targetX - _x;
        var dy = _targetY - _y;
        if (Math.Abs(dx) < 0.0004 && Math.Abs(dy) < 0.0004)
        {
            if (_settled) return; // au repos : zéro travail
            _x = _targetX;
            _y = _targetY;
            _settled = true;
        }
        else
        {
            _x += dx * k;
            _y += dy * k;
            _settled = false;
        }

        _forest.Look(_x, _y);
        foreach (var layer in _layers)
        {
            layer.Pointer.X = -_x * layer.Depth * _w;
            layer.Pointer.Y = -_y * layer.Depth * 28 * _sy;
        }
    }

    // ------------------------------------------------------------------ pinceaux

    private static readonly Brush FarFill = Theme.Gradient(0, 0, 0, 1, (0, Theme.C("#5a2a2c")), (0.5, Theme.C("#34161f")), (1, Theme.C("#1c0d14")));
    private static readonly Brush MidFill = Theme.Gradient(0, 0, 0, 1, (0, Theme.C("#3a1a22")), (0.5, Theme.C("#20101a")), (1, Theme.C("#120a10")));
    private static readonly Brush NearFill = Theme.Gradient(0, 0, 0, 1, (0, Theme.C("#1e0f15")), (1, Theme.C("#0b0709")));
    private static readonly Brush FarTreeFill = Theme.Gradient(0, 0, 0, 1, (0, Theme.C("#2a1219")), (1, Theme.C("#150a10")));
    private static readonly Brush TreeFill = Theme.Gradient(0, 0, 0, 1, (0, Theme.C("#0e090c")), (1, Theme.C("#050305")));

    private static BitmapCache Cache(double scale = 1.25) => new() { RenderAtScale = scale };

    // ------------------------------------------------------------------ construction

    private void AddLayer(Canvas host, Geometry geometry, Brush fill, double depth, double drift, double driftSeconds)
    {
        var path = new Shapes.Path { Data = geometry, Fill = fill, IsHitTestVisible = false };
        var pointer = new TranslateTransform();
        var driftTransform = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(driftTransform);
        group.Children.Add(pointer);
        path.RenderTransform = group;
        path.CacheMode = Cache();
        host.Children.Add(path);
        _layers.Add(new Layer { Pointer = pointer, Depth = depth });

        if (drift > 0 && driftSeconds > 0)
        {
            Anim.Loop(driftTransform, TranslateTransform.XProperty,
                new DoubleAnimation(-drift * _sx, drift * _sx, new Duration(TimeSpan.FromSeconds(driftSeconds))) { AutoReverse = true, EasingFunction = Anim.Sine }, Group);
        }
    }

    private void AddStars(Canvas host)
    {
        var rnd = new Random(99);
        var group = new GeometryGroup();
        var count = (int)(46 * _sx * _sy);
        for (var i = 0; i < Math.Max(30, count); i++)
            group.Children.Add(new EllipseGeometry(new Point(rnd.NextDouble() * _w, rnd.NextDouble() * 120 * _sy), 0.5 + rnd.NextDouble() * 0.7, 0.5 + rnd.NextDouble() * 0.7));
        group.Freeze();
        var stars = new Shapes.Path { Data = group, Fill = Theme.WithAlpha(Color.FromRgb(255, 235, 215), 140), IsHitTestVisible = false, CacheMode = Cache() };
        host.Children.Add(stars);
        stars.Opacity = 0.7;
    }

    private void AddSun(Canvas host)
    {
        double cx = 560 * _sx, cy = 214 * _sy, size = 640 * Math.Max(_sx, _sy);
        var glow = new Shapes.Ellipse
        {
            Width = size, Height = size,
            Fill = Theme.Radial(Color.FromArgb(210, 255, 154, 69), Color.FromArgb(0, 255, 110, 43)),
            IsHitTestVisible = false, CacheMode = Cache(),
        };
        Canvas.SetLeft(glow, cx - size / 2);
        Canvas.SetTop(glow, cy - size / 2);
        host.Children.Add(glow);
        glow.Opacity = 0.92;

        var coreSize = 74 * Math.Max(_sx, _sy);
        var core = new Shapes.Ellipse
        {
            Width = coreSize, Height = coreSize,
            Fill = Theme.Radial(Color.FromRgb(255, 240, 205), Color.FromArgb(0, 255, 190, 110), radius: 0.52),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(core, cx - coreSize / 2);
        Canvas.SetTop(core, cy - coreSize / 2);
        host.Children.Add(core);
    }

    private void AddRays(Canvas host)
    {
        // Rayons de lumière derrière les crêtes : triangles étroits qui respirent à des rythmes différents.
        double cx = 560 * _sx, cy = 214 * _sy, length = 460 * Math.Max(_sx, _sy);
        var angles = new[] { -58.0, -34, -12, 10, 33, 56 };
        for (var i = 0; i < angles.Length; i++)
        {
            var ray = new Shapes.Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(-26, -length), new Point(26, -length) },
                Fill = Theme.Gradient(0, 1, 0, 0, (0, Color.FromArgb(70, 255, 170, 90)), (1, Color.FromArgb(0, 255, 170, 90))),
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(angles[i]),
                Opacity = 0.5,
                CacheMode = Cache(1),
            };
            Canvas.SetLeft(ray, cx);
            Canvas.SetTop(ray, cy);
            host.Children.Add(ray);
            ray.Opacity = 0.35;
        }
    }

    private void AddMist(Canvas host, double y, double alpha)
    {
        var mist = new Shapes.Rectangle
        {
            Width = _w + _bleed * 2, Height = 80 * _sy,
            Fill = Theme.Gradient(0, 0, 0, 1,
                (0, Color.FromArgb(0, 255, 150, 90)),
                (0.5, Color.FromArgb((byte)(255 * alpha), 255, 150, 90)),
                (1, Color.FromArgb(0, 255, 150, 90))),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(mist, -_bleed);
        Canvas.SetTop(mist, y);
        var drift = new TranslateTransform();
        mist.RenderTransform = drift;
        mist.CacheMode = Cache(1);
        host.Children.Add(mist);
        Anim.Loop(drift, TranslateTransform.XProperty,
            new DoubleAnimation(-40 * _sx, 40 * _sx, new Duration(TimeSpan.FromSeconds(24 + y / 10))) { AutoReverse = true, EasingFunction = Anim.Sine }, Group);
    }

    private void AddParachutes(Canvas host)
    {
        // (x relatif, échelle, durée de chute, décalage de phase)
        var chutes = new (double x, double scale, double seconds, double phase)[]
        {
            (610, 1.00, 62, 0.00), (730, 0.72, 78, 0.35), (500, 0.55, 90, 0.62), (790, 0.46, 104, 0.18), (420, 0.40, 118, 0.80),
        };
        var grow = Math.Sqrt(Math.Min(_sx, _sy) < 1 ? 1 : Math.Min(_sx, _sy));

        foreach (var (x, scale, seconds, phase) in chutes)
        {
            var chute = BuildParachute(scale * grow);
            Canvas.SetLeft(chute, x * _sx);
            Canvas.SetTop(chute, -50);
            var move = new TranslateTransform();
            var sway = new RotateTransform(0, 0, 18);
            var group = new TransformGroup();
            group.Children.Add(sway);
            group.Children.Add(move);
            chute.RenderTransform = group;
            chute.CacheMode = Cache(2);
            host.Children.Add(chute);

            var fall = new DoubleAnimation(0, _h + 90, new Duration(TimeSpan.FromSeconds(seconds * Math.Max(1, _sy * 0.8))))
            {
                BeginTime = TimeSpan.FromSeconds(-seconds * phase),
            };
            Anim.Loop(move, TranslateTransform.YProperty, fall, Group);
            Anim.Loop(move, TranslateTransform.XProperty,
                new DoubleAnimation(-22, 22, new Duration(TimeSpan.FromSeconds(7 + scale * 3))) { AutoReverse = true, EasingFunction = Anim.Sine, BeginTime = TimeSpan.FromSeconds(-phase * 6) }, Group);
            Anim.Loop(sway, RotateTransform.AngleProperty,
                new DoubleAnimation(-5, 5, new Duration(TimeSpan.FromSeconds(3.6 + scale * 2))) { AutoReverse = true, EasingFunction = Anim.Sine }, Group);
        }
    }

    private static FrameworkElement BuildParachute(double scale)
    {
        var canopy = Geometry.Parse("M-17 0C-17-16 17-16 17 0 11-3.4 6-5 0-1.6-6-5-11-3.4-17 0Z");
        var lines = Geometry.Parse("M-17 0 0 22M-6 -1.4 0 22M6 -1.4 0 22M17 0 0 22");
        var body = Geometry.Parse("M-2 22h4v7h-4z");
        canopy.Freeze(); lines.Freeze(); body.Freeze();

        var rim = Theme.WithAlpha(Theme.EmberColor, 120);
        var silhouette = Theme.WithAlpha(Theme.C("#0c070a"), 255);
        var holder = new Canvas { Width = 34, Height = 30, IsHitTestVisible = false };
        holder.Children.Add(new Shapes.Path { Data = lines, Stroke = Theme.WithAlpha(Color.FromRgb(20, 10, 12), 200), StrokeThickness = 0.8 });
        holder.Children.Add(new Shapes.Path { Data = canopy, Fill = silhouette, Stroke = rim, StrokeThickness = 0.9 });
        holder.Children.Add(new Shapes.Path { Data = body, Fill = silhouette });
        foreach (UIElement child in holder.Children)
        {
            Canvas.SetLeft(child, 17);
            Canvas.SetTop(child, 16);
        }
        holder.LayoutTransform = new ScaleTransform(scale, scale);
        return holder;
    }

    // ------------------------------------------------------------------ géométrie procédurale

    private static Geometry Ridge(int seed, double x0, double x1, double baseY, double amplitude, double roughness, double bottom, double treeH = 0)
    {
        var rnd = new Random(seed);
        const int segments = 240;
        var heights = new double[segments + 1];
        heights[0] = (rnd.NextDouble() - 0.5) * amplitude;
        heights[segments] = (rnd.NextDouble() - 0.5) * amplitude;
        Displace(heights, 0, segments, amplitude, roughness, rnd);
        for (var i = 0; i <= segments; i++) heights[i] += (rnd.NextDouble() - 0.5) * amplitude * 0.05; // texture fine de crête

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x0, bottom), true, true);
            for (var i = 0; i <= segments; i++)
                ctx.LineTo(new Point(x0 + (x1 - x0) * i / segments, baseY - heights[i]), true, false);
            ctx.LineTo(new Point(x1, bottom), true, false);
            // lisière de conifères sur la crête : casse la ligne lisse et donne l'échelle
            if (treeH > 0)
            {
                var fringe = new Random(seed * 7 + 3);
                for (var i = 2; i < segments - 1; i++)
                {
                    if (fringe.NextDouble() > 0.62) continue;
                    var px = x0 + (x1 - x0) * i / segments;
                    var h = treeH * (0.55 + fringe.NextDouble() * 0.95);
                    Pine(ctx, px, baseY - heights[i] + 2, h, h * (0.30 + fringe.NextDouble() * 0.08), fringe, tiers: 4);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static void Displace(double[] h, int lo, int hi, double amplitude, double roughness, Random rnd)
    {
        if (hi - lo < 2) return;
        var mid = (lo + hi) / 2;
        h[mid] = (h[lo] + h[hi]) / 2 + (rnd.NextDouble() * 2 - 1) * amplitude;
        Displace(h, lo, mid, amplitude * roughness, roughness, rnd);
        Displace(h, mid, hi, amplitude * roughness, roughness, rnd);
    }

    // ------------------------------------------------------------------ forêt

    /// <summary>Lisière lointaine : petits conifères serrés, silhouette douce et régulière (profondeur).</summary>
    private Geometry FarTreeline(int seed, double x0, double x1, double bottom)
    {
        var rnd = new Random(seed);
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geometry.Open())
        {
            for (var x = x0; x < x1; x += (7 + rnd.NextDouble() * 7) * _sx)
            {
                var h = (26 + rnd.NextDouble() * 20) * _sy;
                Pine(ctx, x, bottom - 26 * _sy, h, h * (0.30 + rnd.NextDouble() * 0.05), rnd, tiers: 4);
            }
            ctx.BeginFigure(new Point(x0, bottom - 28 * _sy), true, true);
            ctx.LineTo(new Point(x1, bottom - 28 * _sy), true, false);
            ctx.LineTo(new Point(x1, bottom + 10), true, false);
            ctx.LineTo(new Point(x0, bottom + 10), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Un pin : étages de branches qui retombent, bords légèrement courbes, asymétrie gauche/droite,
    /// tronc visible. Plus fin et plus naturel qu'une pile de triangles.</summary>
    private static void Pine(StreamGeometryContext ctx, double x, double baseY, double height, double width, Random rnd, int tiers)
    {
        var top = baseY - height;
        var trunk = Math.Max(1.2, width * 0.07);
        double Tip(int i) => top + height * (0.10 + 0.84 * (i + 1) / tiers);
        double Reach(int i, double j) => width * (0.16 + 0.84 * Math.Pow((i + 1.0) / tiers, 0.85)) * (1 - j + rnd.NextDouble() * j * 2);

        // côté droit, de la pointe au pied
        ctx.BeginFigure(new Point(x, top), true, true);
        var prev = new Point(x, top);
        for (var i = 0; i < tiers; i++)
        {
            var tip = new Point(x + Reach(i, 0.10), Tip(i));
            ctx.QuadraticBezierTo(new Point(prev.X + (tip.X - prev.X) * 0.55, prev.Y + (tip.Y - prev.Y) * 0.15), tip, true, false);
            var nextY = i + 1 < tiers ? Tip(i + 1) : baseY - height * 0.04;
            var twig = new Point(x + (tip.X - x) * (0.58 + rnd.NextDouble() * 0.12), tip.Y + (nextY - tip.Y) * (0.10 + rnd.NextDouble() * 0.08));
            ctx.LineTo(twig, true, false);
            var notch = new Point(x + (tip.X - x) * (0.26 + rnd.NextDouble() * 0.10), tip.Y + (nextY - tip.Y) * 0.34);
            ctx.QuadraticBezierTo(new Point(tip.X - (tip.X - notch.X) * 0.35, tip.Y + 1.2), notch, true, false);
            prev = notch;
        }
        ctx.LineTo(new Point(x + trunk, prev.Y), true, false);
        ctx.LineTo(new Point(x + trunk, baseY), true, false);
        ctx.LineTo(new Point(x - trunk, baseY), true, false);
        ctx.LineTo(new Point(x - trunk, prev.Y), true, false);

        // côté gauche, du pied à la pointe (tirage différent : arbre asymétrique)
        for (var i = tiers - 1; i >= 0; i--)
        {
            var nextY = i + 1 < tiers ? Tip(i + 1) : baseY - height * 0.04;
            var tip = new Point(x - Reach(i, 0.12), Tip(i));
            var notch = new Point(x - (x - tip.X) * (0.26 + rnd.NextDouble() * 0.10), tip.Y + (nextY - tip.Y) * 0.34);
            ctx.LineTo(notch, true, false);
            ctx.QuadraticBezierTo(new Point(tip.X + (notch.X - tip.X) * 0.35, tip.Y + 1.2), tip, true, false);
        }    }

    // ------------------------------------------------------------------ ambiance cinéma

    /// <summary>Nuages allongés éclairés par en dessous (reflet du soleil bas) qui dérivent lentement.</summary>
    private void AddClouds(Canvas host)
    {
        var specs = new (double x, double y, double w, double h, double a, double seconds)[]
        {
            (60, 70, 380, 34, 0.30, 70), (330, 112, 520, 40, 0.42, 90), (610, 58, 360, 28, 0.26, 80), (140, 138, 420, 30, 0.36, 100),
        };
        foreach (var (x, y, w, h, a, seconds) in specs)
        {
            var cloud = new Shapes.Ellipse
            {
                Width = w * _sx, Height = h * _sy, Opacity = a, IsHitTestVisible = false,
                Fill = Theme.Radial(Color.FromArgb(255, 255, 168, 96), Color.FromArgb(0, 120, 40, 40), 0.5, 0.95, 0.62),
                CacheMode = Cache(1),
            };
            Canvas.SetLeft(cloud, x * _sx - 60 * _sx);
            Canvas.SetTop(cloud, y * _sy);
            var drift = new TranslateTransform();
            cloud.RenderTransform = drift;
            host.Children.Add(cloud);
            Anim.Loop(drift, TranslateTransform.XProperty,
                new DoubleAnimation(-70 * _sx, 70 * _sx, new Duration(TimeSpan.FromSeconds(seconds))) { AutoReverse = true, EasingFunction = Anim.Sine, BeginTime = TimeSpan.FromSeconds(-seconds / 3) }, Group);
        }
    }

    /// <summary>Étalonnage final : vignettage doux + grain de pellicule fixe (une petite tuile répétée par le GPU).</summary>
    private void AddFilmLook()
    {
        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(105, 4, 3, 6)) { RadiusX = 0.95, RadiusY = 1.0, GradientOrigin = new Point(0.58, 0.55), Center = new Point(0.58, 0.55) },
        });

        const int size = 128;
        var pixels = new byte[size * size * 4];
        var rnd = new Random(1337);
        for (var i = 0; i < size * size; i++)
        {
            var v = (byte)rnd.Next(110, 255);
            var alpha = (byte)rnd.Next(0, 22);
            pixels[i * 4] = (byte)(v * alpha / 255);     // BGRA prémultiplié
            pixels[i * 4 + 1] = (byte)(v * alpha / 255);
            pixels[i * 4 + 2] = (byte)(v * alpha / 255);
            pixels[i * 4 + 3] = alpha;
        }
        var noise = System.Windows.Media.Imaging.BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        noise.Freeze();
        var tile = new ImageBrush(noise) { TileMode = TileMode.Tile, Stretch = Stretch.None, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, size, size) };
        tile.Freeze();
        Children.Add(new Border { IsHitTestVisible = false, Background = tile, Opacity = 0.32 });
    }

}
