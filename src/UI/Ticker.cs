using System.Windows.Media;

namespace COTK.Next.UI;

internal interface IFrameUpdatable
{
    /// <summary>Vrai si l'élément est visible et doit être mis à jour (les éléments cachés ne coûtent rien).</summary>
    bool IsActive { get; }

    void Update(double dt);
}

/// <summary>Unique boucle de rendu du launcher (braises, parallaxe…), plafonnée à ~30 images/s et
/// totalement endormie quand la fenêtre est réduite ou inactive. Un seul abonnement à
/// CompositionTarget.Rendering quelle que soit la quantité d'effets.</summary>
internal static class Ticker
{
    private const double MinStep = 1.0 / 31.0;

    private static readonly List<IFrameUpdatable> Items = new();
    private static bool _hooked;
    private static bool _paused;
    private static TimeSpan _last = TimeSpan.MinValue;

    public static bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            Sync();
        }
    }

    public static void Add(IFrameUpdatable item)
    {
        if (!Items.Contains(item)) Items.Add(item);
        Sync();
    }

    public static void Remove(IFrameUpdatable item)
    {
        Items.Remove(item);
        Sync();
    }

    private static void Sync()
    {
        var want = !_paused && Items.Count > 0 && Anim.Enabled;
        if (want && !_hooked)
        {
            _last = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnRendering;
            _hooked = true;
        }
        else if (!want && _hooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_last == TimeSpan.MinValue) { _last = now; return; }
        var dt = (now - _last).TotalSeconds;
        if (dt < MinStep) return;
        _last = now;
        dt = Math.Min(dt, 0.1);
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (item.IsActive) item.Update(dt);
        }
    }

    /// <summary>Réévalue l'abonnement (ex. quand l'utilisateur change « réduire les animations »).</summary>
    public static void Refresh() => Sync();
}
