using System.Windows;
using System.Windows.Media;

namespace COTK.Next.UI;

/// <summary>Braises qui montent (effet signature). Une seule surface dessinée à ~30 images/s : 40 points, des
/// pinceaux gelés pré-calculés par palier d'opacité, aucune allocation par image. Dort quand elle est cachée,
/// quand la fenêtre est inactive, ou quand « réduire les animations » est actif.</summary>
internal sealed class Embers : FrameworkElement, IFrameUpdatable
{
    private struct Particle
    {
        public double X, Y, Vx, Vy, Size, Phase, Life, Span;
        public int Hue;
    }

    private const int AlphaLevels = 8;
    private readonly Particle[] _particles;
    private readonly Random _random;
    private readonly Brush[,] _core = new Brush[3, AlphaLevels];
    private readonly Brush[,] _halo = new Brush[3, AlphaLevels];
    private readonly double _speed;

    public Embers(int count = 40, double speed = 1.0, int seed = 7)
    {
        _random = new Random(seed);
        _particles = new Particle[count];
        _speed = speed;
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;

        var hues = new[] { Theme.EmberHiColor, Theme.EmberColor, Theme.GoldColor };
        for (var h = 0; h < hues.Length; h++)
        {
            for (var a = 0; a < AlphaLevels; a++)
            {
                var alpha = (a + 1) / (double)AlphaLevels;
                _core[h, a] = Freeze(new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), hues[h].R, hues[h].G, hues[h].B)));
                _halo[h, a] = Freeze(new SolidColorBrush(Color.FromArgb((byte)(46 * alpha), hues[h].R, hues[h].G, hues[h].B)));
            }
        }

        for (var i = 0; i < _particles.Length; i++) Respawn(ref _particles[i], initial: true);
        Loaded += (_, _) => Ticker.Add(this);
        Unloaded += (_, _) => Ticker.Remove(this);
    }

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public bool IsActive => IsVisible && ActualWidth > 0 && ActualHeight > 0;

    private void Respawn(ref Particle p, bool initial)
    {
        p.X = _random.NextDouble();
        p.Y = initial ? _random.NextDouble() : 1.04 + _random.NextDouble() * 0.12;
        p.Vy = (0.035 + _random.NextDouble() * 0.07) * _speed;       // fraction de hauteur par seconde
        p.Vx = (_random.NextDouble() - 0.35) * 0.012;
        p.Size = 0.8 + Math.Pow(_random.NextDouble(), 2.2) * 2.6;
        p.Phase = _random.NextDouble() * Math.PI * 2;
        p.Span = 5 + _random.NextDouble() * 9;
        p.Life = initial ? _random.NextDouble() * p.Span : 0;
        p.Hue = _random.Next(3);
    }

    public void Update(double dt)
    {
        for (var i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            p.Life += dt;
            p.Phase += dt * (0.9 + p.Size * 0.25);
            p.Y -= p.Vy * dt;
            p.X += (p.Vx + Math.Sin(p.Phase) * 0.014) * dt;
            if (p.Y < -0.06 || p.Life > p.Span || p.X < -0.05 || p.X > 1.05) Respawn(ref p, initial: false);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        foreach (var p in _particles)
        {
            // fondu d'entrée/sortie + scintillement léger
            var t = Math.Clamp(p.Life / p.Span, 0, 1);
            var envelope = Math.Sin(t * Math.PI);
            var flicker = 0.78 + 0.22 * Math.Sin(p.Phase * 3.1);
            var level = (int)Math.Clamp(envelope * flicker * AlphaLevels, 0, AlphaLevels - 1);
            var center = new Point(p.X * w, p.Y * h);
            if (p.Size > 1.9) dc.DrawEllipse(_halo[p.Hue, level], null, center, p.Size * 3.4, p.Size * 3.4);
            dc.DrawEllipse(_core[p.Hue, level], null, center, p.Size * 0.62, p.Size * 0.62);
        }
    }
}
