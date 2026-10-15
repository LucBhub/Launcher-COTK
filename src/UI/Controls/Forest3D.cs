using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace COTK.Next.UI;

/// <summary>Forêt en vraie 3D (Viewport3D, accélérée GPU) : pins low-poly à facettes, éclairés par une lumière
/// chaude de crépuscule, une lumière de remplissage et un contre-jour. Les arbres ont du volume, des faces
/// claires et sombres, et la caméra bouge avec la souris : la parallaxe est réelle, pas un décalage de calques.</summary>
internal sealed class Forest3D : Grid
{
    private readonly Viewport3D _viewport = new();
    private readonly Image _snapshot = new() { Stretch = Stretch.None, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly System.Windows.Threading.DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly PerspectiveCamera _camera;
    private bool _live = true;
    private double _lastX, _lastY;

    public Forest3D(double width, double height)
    {
        Width = width;
        Height = height * 0.52;
        VerticalAlignment = VerticalAlignment.Bottom;
        IsHitTestVisible = false;
        ClipToBounds = true;
        _camera = new PerspectiveCamera(new Point3D(0, 3.1, 16), new Vector3D(0, 0.02, -1), new Vector3D(0, 1, 0), 36);
        _viewport.Camera = _camera;
        _viewport.IsHitTestVisible = false;

        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(62, 40, 50)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(255, 176, 108), new Vector3D(0.55, -0.35, -0.75)));   // soleil bas, de côté
        scene.Children.Add(new DirectionalLight(Color.FromRgb(150, 64, 30), new Vector3D(-0.25, -0.10, 0.95)));    // contre-jour : liseré chaud
        scene.Children.Add(new DirectionalLight(Color.FromRgb(36, 30, 56), new Vector3D(-0.6, -0.5, -0.3)));       // remplissage froid

        // Bandes de profondeur : plus un arbre est loin, plus il est voilé par la brume chaude du couchant
        // (couleur qui rejoint celle des montagnes) — c'est la perspective atmosphérique qui crée la distance.
        var bands = new (double near, double far, int count, Color color)[]
        {
            (1, 10, 70, Color.FromRgb(33, 40, 31)),
            (10, 22, 95, Color.FromRgb(55, 43, 38)),
            (22, 45, 150, Color.FromRgb(88, 49, 44)),
            (45, 95, 220, Color.FromRgb(118, 60, 50)),
        };
        var rnd = new Random(77);
        foreach (var (zNear, zFar, count, color) in bands)
        {
            var mesh = new MeshGeometry3D();
            for (var i = 0; i < count; i++)
            {
                var z = -(zNear + rnd.NextDouble() * (zFar - zNear));
                var x = (rnd.NextDouble() * 2 - 1) * (9 + -z * 0.75);
                if (Math.Abs(x) < 2.4 && z > -16 && rnd.NextDouble() < 0.85) continue;   // clairière centrale
                var h = 1.7 + rnd.NextDouble() * 2.5 + (Math.Abs(x) > 7 && zFar <= 22 ? 0.9 : 0) + (zFar > 45 ? 1.4 : 0);
                Pine(mesh, x, z, h, h * (0.24 + rnd.NextDouble() * 0.05), rnd);
            }
            scene.Children.Add(Model(mesh, color));
        }
        var ground = new MeshGeometry3D();
        Quad(ground, new Point3D(-70, 0, 8), new Point3D(70, 0, 8), new Point3D(70, 0, -45), new Point3D(-70, 0, -45));
        scene.Children.Add(Model(ground, Color.FromRgb(14, 10, 12)));

        _viewport.Children.Add(new ModelVisual3D { Content = scene });
        Children.Add(_viewport);
        Children.Add(_snapshot);
        _snapshot.Visibility = Visibility.Collapsed;

        // Rendu 3D logiciel coûteux : quand la caméra ne bouge pas, l'image identique est figée en bitmap
        // (composée par le GPU) et la scène 3D vivante n'est réactivée que pendant le mouvement de la souris.
        _settle.Tick += (_, _) => { _settle.Stop(); Freeze(); };
        Loaded += (_, _) => RequestFreeze();
        SizeChanged += (_, _) => RequestFreeze();
        IsVisibleChanged += (_, _) => RequestFreeze();
    }

    private void RequestFreeze()
    {
        if (!_live || !IsVisible) return;
        _settle.Stop();
        _settle.Start();
    }

    private void Freeze()
    {
        if (!_live || ActualWidth < 4 || ActualHeight < 4) return;
        _viewport.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(_viewport);
        bitmap.Freeze();
        _snapshot.Source = bitmap;
        _snapshot.Width = ActualWidth;
        _snapshot.Height = ActualHeight;
        _snapshot.Visibility = Visibility.Visible;
        _viewport.Visibility = Visibility.Collapsed;
        _live = false;
    }

    /// <summary>La caméra suit la souris (px/py de -1 à 1) : décalage latéral et léger regard.</summary>
    public void Look(double px, double py)
    {
        if (Math.Abs(px - _lastX) < 0.0005 && Math.Abs(py - _lastY) < 0.0005) return;   // caméra inchangée : on garde l'image figée
        _lastX = px;
        _lastY = py;
        if (!_live)
        {
            _viewport.Visibility = Visibility.Visible;
            _snapshot.Visibility = Visibility.Collapsed;
            _live = true;
        }
        _settle.Stop();
        _settle.Start();
        _camera.Position = new Point3D(-px * 1.5, 3.1 - py * 0.3, 16);
        _camera.LookDirection = new Vector3D(px * 0.07, 0.02, -1);
    }

    private static GeometryModel3D Model(MeshGeometry3D mesh, Color color)
    {
        mesh.Freeze();
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        material.Freeze();
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        model.Freeze();
        return model;
    }

    private static void Tri(MeshGeometry3D m, Point3D a, Point3D b, Point3D c)
    {
        var i = m.Positions.Count;     // sommets non partagés : ombrage à facettes
        m.Positions.Add(a); m.Positions.Add(b); m.Positions.Add(c);
        m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
    }

    private static void Quad(MeshGeometry3D m, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        Tri(m, a, b, c);
        Tri(m, a, c, d);
    }

    /// <summary>Pin : tronc, puis étages de cônes dont la base est irrégulière et retombante.</summary>
    private static void Pine(MeshGeometry3D m, double x, double z, double h, double radius, Random rnd)
    {
        var trunk = radius * 0.11;
        var t0 = new Point3D(x - trunk, 0, z - trunk); var t1 = new Point3D(x + trunk, 0, z - trunk);
        var t2 = new Point3D(x + trunk, 0, z + trunk); var t3 = new Point3D(x - trunk, 0, z + trunk);
        var top = h * 0.30;
        Quad(m, t0, t1, new Point3D(x + trunk * 0.7, top, z - trunk * 0.7), new Point3D(x - trunk * 0.7, top, z - trunk * 0.7));
        Quad(m, t1, t2, new Point3D(x + trunk * 0.7, top, z + trunk * 0.7), new Point3D(x + trunk * 0.7, top, z - trunk * 0.7));
        Quad(m, t2, t3, new Point3D(x - trunk * 0.7, top, z + trunk * 0.7), new Point3D(x + trunk * 0.7, top, z + trunk * 0.7));

        const int tiers = 7, sides = 9;
        var phase = rnd.NextDouble() * Math.PI;
        for (var t = 0; t < tiers; t++)
        {
            var f = t / (double)tiers;
            var baseY = h * (0.14 + 0.70 * f);
            var apex = new Point3D(x + (rnd.NextDouble() - 0.5) * radius * 0.06, baseY + h * 0.30, z);
            var r = radius * (1.0 - f * 0.80);
            var ring = new Point3D[sides];
            for (var s = 0; s < sides; s++)
            {
                var angle = phase + t * 0.7 + (s + (rnd.NextDouble() - 0.5) * 0.45) * Math.PI * 2 / sides;
                var rr = r * (0.80 + rnd.NextDouble() * 0.40);
                ring[s] = new Point3D(x + Math.Cos(angle) * rr, baseY - rnd.NextDouble() * h * 0.05, z + Math.Sin(angle) * rr);
            }
            for (var s = 0; s < sides; s++) Tri(m, apex, ring[(s + 1) % sides], ring[s]);
        }
    }
}
