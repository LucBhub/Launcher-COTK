using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Animations du launcher. Principes : (1) tout passe par l'interrupteur « réduire les animations »
/// (accessibilité + PC modestes) ; (2) les animations fléchées finissent par écrire la vraie valeur puis
/// retirent l'animation, sinon une propriété resterait « figée » ; (3) les boucles infinies utilisent des
/// horloges qu'on met en pause quand la fenêtre est réduite ou inactive (zéro CPU en arrière-plan).</summary>
internal static class Anim
{
    public static bool Enabled => !Settings.ReduceMotion;

    public static readonly IEasingFunction Out = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction InOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });
    public static readonly IEasingFunction Expo = Freeze(new ExponentialEase { Exponent = 5, EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction Back = Freeze(new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction Linear = Freeze(new PowerEase { Power = 1, EasingMode = EasingMode.EaseInOut });
    public static readonly IEasingFunction Sine = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });

    private static bool _suspended;

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Duration Ms(int ms) => new(TimeSpan.FromMilliseconds(Math.Max(1, ms)));

    // ------------------------------------------------------------------ valeur → valeur

    public static void To(IAnimatable target, DependencyProperty property, double to, int ms = 220, int delayMs = 0, IEasingFunction? ease = null, Action? done = null)
    {
        var obj = (DependencyObject)target;
        if (!Enabled || ms <= 0)
        {
            target.BeginAnimation(property, null);
            obj.SetValue(property, to);
            done?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(to, Ms(ms))
        {
            EasingFunction = ease ?? Out,
            BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, delayMs)),
            FillBehavior = FillBehavior.HoldEnd,
        };
        animation.Completed += (_, _) =>
        {
            // La valeur finale devient la vraie valeur ; on retire l'animation pour ne rien figer.
            target.BeginAnimation(property, null);
            obj.SetValue(property, to);
            done?.Invoke();
        };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Fade(UIElement element, double to, int ms = 220, int delayMs = 0, Action? done = null)
        => To(element, UIElement.OpacityProperty, to, ms, delayMs, Out, done);

    /// <summary>Apparition : fondu + glissement vers le haut. L'état initial est posé tout de suite.</summary>
    public static void Rise(UIElement element, int delayMs = 0, double dy = 14, int ms = 520)
    {
        if (!Enabled)
        {
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            return;
        }

        var shift = new TranslateTransform(0, dy);
        element.RenderTransform = shift;
        element.Opacity = 0;
        To(element, UIElement.OpacityProperty, 1, ms, delayMs, Out);
        To(shift, TranslateTransform.YProperty, 0, ms + 80, delayMs, Expo);
    }

    /// <summary>Apparition en cascade d'un groupe d'éléments.</summary>
    public static void Reveal(IEnumerable<UIElement> elements, int startMs = 0, int stepMs = 55, double dy = 14)
    {
        var i = 0;
        foreach (var element in elements) Rise(element, startMs + i++ * stepMs, dy);
    }

    public static void Pop(UIElement element, int delayMs = 0, int ms = 420)
    {
        if (!Enabled) { element.Opacity = 1; return; }
        var scale = new ScaleTransform(0.86, 0.86);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        element.Opacity = 0;
        To(element, UIElement.OpacityProperty, 1, ms, delayMs, Out);
        To(scale, ScaleTransform.ScaleXProperty, 1, ms, delayMs, Back);
        To(scale, ScaleTransform.ScaleYProperty, 1, ms, delayMs, Back);
    }

    // ------------------------------------------------------------------ nombres

    public static readonly DependencyProperty NumberProperty = DependencyProperty.RegisterAttached(
        "Number", typeof(double), typeof(Anim), new PropertyMetadata(0.0, OnNumberChanged));

    private static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached(
        "Format", typeof(Func<double, string>), typeof(Anim));

    private static void OnNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock text && d.GetValue(FormatProperty) is Func<double, string> format)
            text.Text = format((double)e.NewValue);
    }

    /// <summary>Compteur animé (0 → valeur). Le texte final est toujours exact.</summary>
    public static void CountUp(TextBlock text, double to, Func<double, string> format, int ms = 1000, int delayMs = 0)
    {
        text.SetValue(FormatProperty, format);
        if (!Enabled)
        {
            text.BeginAnimation(NumberProperty, null);
            text.Text = format(to);
            return;
        }
        text.SetValue(NumberProperty, 0.0);
        text.Text = format(0);
        To(text, NumberProperty, to, ms, delayMs, Expo, () => text.Text = format(to));
    }

    // ------------------------------------------------------------------ couleurs

    public static void ColorTo(SolidColorBrush brush, Color to, int ms = 180)
    {
        if (brush.IsFrozen) return;
        if (!Enabled)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = to;
            return;
        }
        var animation = new ColorAnimation(to, Ms(ms)) { EasingFunction = Out };
        animation.Completed += (_, _) =>
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = to;
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    // ------------------------------------------------------------------ boucles (pausables, par groupe)

    /// <summary>Boucle infinie sur une horloge pausable, rattachée à un groupe (une vue, une scène…).
    /// Retourne l'horloge pour pouvoir l'arrêter.</summary>
    public static AnimationClock? Loop(IAnimatable target, DependencyProperty property, AnimationTimeline timeline, LoopGroup? group = null)
    {
        if (!Enabled) return null;
        timeline.RepeatBehavior = RepeatBehavior.Forever;
        Timeline.SetDesiredFrameRate(timeline, 30);
        var clock = (AnimationClock)timeline.CreateClock(true);
        (group ?? LoopGroup.Shared).Add(clock);
        target.ApplyAnimationClock(property, clock);
        return clock;
    }

    public static AnimationClock? Pulse(UIElement element, double min = 0.35, double max = 1, int ms = 1600, LoopGroup? group = null)
    {
        var clock = Loop(element, UIElement.OpacityProperty,
            new DoubleAnimation(min, max, Ms(ms)) { AutoReverse = true, EasingFunction = Sine }, group);
        if (element is FrameworkElement owner && clock is not null)
        {
            RoutedEventHandler? release = null;
            release = (_, _) =>
            {
                owner.Unloaded -= release;
                StopClock(clock);
            };
            owner.Unloaded += release;
        }
        return clock;
    }

    public static AnimationClock? Spin(UIElement element, int ms = 1100, LoopGroup? group = null)
    {
        var rotate = new RotateTransform(0);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = rotate;
        return Loop(rotate, RotateTransform.AngleProperty, new DoubleAnimation(0, 360, Ms(ms)), group);
    }

    public static void StopLoop(IAnimatable target, DependencyProperty property, AnimationClock? clock)
    {
        if (clock is null) return;
        LoopGroup.RemoveEverywhere(clock);
        clock.Controller?.Stop();
        target.ApplyAnimationClock(property, null);
    }

    public static void StopClock(AnimationClock? clock)
    {
        if (clock is null) return;
        LoopGroup.RemoveEverywhere(clock);
        clock.Controller?.Stop();
    }

    /// <summary>Fenêtre réduite ou inactive : toutes les boucles dorment (aucun coût CPU).</summary>
    public static bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            LoopGroup.RecomputeAll();
        }
    }
}

/// <summary>Ensemble d'horloges en boucle qui dorment ensemble : une vue cachée ou une scène hors écran
/// ne consomme aucun cycle. L'état effectif = fenêtre active ET groupe actif.</summary>
internal sealed class LoopGroup
{
    // L'ordre compte : All doit exister avant que Shared (dont le constructeur s'y inscrit) soit créé.
    private static readonly List<LoopGroup> All = new();
    public static readonly LoopGroup Shared = new();

    // Observabilité du banc UI : vérifie qu'une navigation répétée ne multiplie pas les horloges.
    internal static int ClockCount => All.Sum(g => g._clocks.Count);

    public void Release()
    {
        Clear();
        lock (All) All.Remove(this);
    }

    private readonly List<AnimationClock> _clocks = new();
    private bool _active = true;

    public LoopGroup()
    {
        lock (All) All.Add(this);
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            Recompute();
        }
    }

    internal void Add(AnimationClock clock)
    {
        lock (_clocks) _clocks.Add(clock);
        if (!ShouldRun) clock.Controller?.Pause();
    }

    /// <summary>Arrête et oublie toutes les horloges (avant de reconstruire un contenu).</summary>
    public void Clear()
    {
        lock (_clocks)
        {
            foreach (var clock in _clocks) clock.Controller?.Stop();
            _clocks.Clear();
        }
    }

    private bool ShouldRun => _active && !Anim.Suspended && Anim.Enabled;

    internal void Recompute()
    {
        var run = ShouldRun;
        lock (_clocks)
        {
            foreach (var clock in _clocks)
            {
                if (clock.Controller is not { } controller) continue;
                if (run) controller.Resume();
                else controller.Pause();
            }
        }
    }

    internal static void RecomputeAll()
    {
        lock (All) foreach (var group in All) group.Recompute();
    }

    internal static void RemoveEverywhere(AnimationClock clock)
    {
        lock (All)
        {
            foreach (var group in All)
                lock (group._clocks) group._clocks.Remove(clock);
        }
    }
}
