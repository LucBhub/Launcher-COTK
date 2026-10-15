using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Accueil : chiffres clés, grande bannière animée avec le bouton d'action, progression de rang, actualités.</summary>
internal sealed class HomeView : View
{
    private readonly Card _statsCard = new(14);
    private readonly Border _hero = new();
    private readonly IScene _scene = SceneFactory.Create("hero", 848, 300, 0.42, 0.95);
    private readonly PlayPanel _play;
    private readonly FrameworkElement _patchLine;
    private readonly TextBlock _patchText;
    private readonly Card _rankCard = new(14);
    private readonly Card _newsCard = new(14);
    private readonly List<FrameworkElement> _reveals = new();
    private LoadStatus _statsShown = LoadStatus.Idle;

    public HomeView(LauncherApp app, INavigator nav) : base(app, nav)
    {
        _patchText = app.Preview ? Ui.Text("",13,Theme.InkDim) : Ui.Raw("",13,Theme.InkDim);
        _play = new PlayPanel(app);
        _patchLine = BuildPatchLine();

        var stack = new StackPanel { Margin = new Thickness(28, 6, 28, 34) };
        stack.Children.Add(_statsCard);
        stack.Children.Add(BuildHero().M(0, 16, 0, 0));
        stack.Children.Add(_patchLine.M(2, 14, 0, 0));
        var rankTitle = SectionTitle("Paliers de rang").M(2, 28, 0, 10);
        stack.Children.Add(rankTitle);
        stack.Children.Add(_rankCard);
        var newsTitle = SectionTitle("Actualités et mises à jour").M(2, 28, 0, 10);
        stack.Children.Add(newsTitle);
        stack.Children.Add(_newsCard);
        Children.Add(Scroller(stack));

        _reveals.AddRange([_statsCard, _hero, _patchLine, rankTitle, _rankCard, newsTitle, _newsCard]);

        RebuildStats();
        RebuildRank();
        RebuildNews();
        RefreshPatchLine();
        _play.Refresh(animate: false);
        _scene.Group.Active = false;
    }

    public override string Title => "Bon retour, ";

    public override string? TitleStrong => App.Session?.Account.Username;

    public override void Activated()
    {
        base.Activated();
        _scene.Group.Active = true;
        Anim.Reveal(_reveals, 0, 70, 16);
        RebuildStats(countUp: true);
        // la scène se « pose » : léger zoom arrière à l'ouverture
        if (Anim.Enabled)
        {
            var scale = new ScaleTransform(1.07, 1.07, 424, 150);
            _scene.Element.RenderTransform = scale;
            Anim.To(scale, ScaleTransform.ScaleXProperty, 1, 1700, 0, Anim.Expo);
            Anim.To(scale, ScaleTransform.ScaleYProperty, 1, 1700, 0, Anim.Expo);
        }
    }

    public override void Deactivated()
    {
        base.Deactivated();
        _scene.Group.Active = false;
        _scene.Pointer(0, 0);
    }

    protected override void Changed(string what)
    {
        switch (what)
        {
            case "profile":
                RebuildStats(countUp: IsCurrent && App.Profile.Status == LoadStatus.Ready && _statsShown != LoadStatus.Ready);
                RebuildRank();
                break;
            case "notes":
                RebuildNews();
                RefreshPatchLine();
                break;
            case "client" or "operation" or "transfer" or "play" or "server":
                _play.Refresh();
                break;
        }
    }

    // ================================================================== bannière

    private FrameworkElement BuildHero()
    {
        _hero.Height = 300;
        _hero.CornerRadius = new CornerRadius(16);
        _hero.BorderBrush = Theme.Line;
        _hero.BorderThickness = new Thickness(1);
        _hero.Background = Theme.Bg;
        RoundClip(_hero, 15);

        var layers = new Grid();
        layers.Children.Add(_scene.Element);
        layers.Children.Add(new Embers(36, 1.0) { IsHitTestVisible = false });
        layers.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = Theme.Gradient(0, 0, 1, 0, (0, Color.FromArgb(238, 6, 6, 8)), (0.48, Color.FromArgb(150, 6, 6, 8)), (0.85, Color.FromArgb(0, 6, 6, 8))),
        });
        layers.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = Theme.Gradient(0, 1, 0, 0, (0, Color.FromArgb(210, 6, 6, 8)), (0.5, Color.FromArgb(0, 6, 6, 8))),
        });

        // ---- texte à gauche ----
        var live = Ui.Dot(Theme.Ember, 7);
        Anim.Pulse(live, 0.3, 1, 1400, Loops);
        var eyebrow = Ui.HStack(live.M(0, 0, 9, 0), Ui.Mono(Ui.Spaced("Pré-saison 3  ·  battle royale"), 10.5, Theme.EmberHi));
        var title = Ui.Display("CROWN OF THE KING", 48, Brushes.White, FontWeights.Bold);
        title.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.6 };
        var sub = Ui.Text("Le dernier survivant porte la couronne. Atterrissez, pillez, survivez — le classement se joue maintenant.", 13.5, Theme.WithAlpha(Color.FromRgb(214, 214, 224), 255), wrap: true);
        sub.MaxWidth = 430;
        var chips = Ui.HStack(HeroChip("SOLO"), HeroChip("150 JOUEURS").M(8, 0, 0, 0), HeroChip("CARTE Z2").M(8, 0, 0, 0));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(34, 0, 0, 32), MaxWidth = 480 };
        text.Children.Add(eyebrow);
        text.Children.Add(title.M(0, 8, 0, 0));
        text.Children.Add(sub.M(0, 8, 0, 0));
        text.Children.Add(chips.M(0, 16, 0, 0));
        layers.Children.Add(text);

        _play.HorizontalAlignment = HorizontalAlignment.Right;
        _play.VerticalAlignment = VerticalAlignment.Bottom;
        _play.Margin = new Thickness(0, 0, 28, 28);
        layers.Children.Add(_play);

        _hero.Child = layers;
        _hero.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(_hero);
            if (_hero.ActualWidth <= 0 || _hero.ActualHeight <= 0) return;
            _scene.Pointer(p.X / _hero.ActualWidth * 2 - 1, p.Y / _hero.ActualHeight * 2 - 1);
        };
        _hero.MouseLeave += (_, _) => _scene.Pointer(0, 0);
        return _hero;
    }

    private static Border HeroChip(string text) => new()
    {
        Background = Theme.WithAlpha(Color.FromRgb(8, 8, 10), 140),
        BorderBrush = Theme.White12,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        Padding = new Thickness(10, 4, 10, 5),
        Child = Ui.Text(text, 10.5, Theme.InkDim, FontWeights.SemiBold),
    };

    private FrameworkElement BuildPatchLine()
    {
        var link = Ui.Text("Lire les notes complètes  →", 13, Theme.EmberHi, FontWeights.SemiBold);
        link.Cursor = Cursors.Hand;
        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.MouseLeave += (_, _) => link.TextDecorations = null;
        link.MouseLeftButtonUp += (_, _) =>
        {
            if (App.Notes.Value is { Count: > 0 } notes) Nav.OpenNote(notes[0]);
        };
        var row = Ui.HStack(_patchText, link.M(10, 0, 0, 0));
        row.Visibility = Visibility.Collapsed;
        return row;
    }

    private void RefreshPatchLine()
    {
        if (App.Notes.Value is { Count: > 0 } notes)
        {
            _patchText.Text = notes[0].Title + (notes[0].Summary.Length > 0 ? " — " + Trim(notes[0].Summary, 80) : "");
            _patchLine.Visibility = Visibility.Visible;
        }
        else
        {
            _patchLine.Visibility = Visibility.Collapsed;
        }
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    // ================================================================== chiffres clés

    private void RebuildStats(bool countUp = false)
    {
        var remote = App.Profile;
        var grid = Ui.Columns(Ui.Star(), Ui.Star(), Ui.Star(), Ui.Star(), Ui.Star());
        grid.Margin = new Thickness(0, 4, 0, 4);
        _statsShown = remote.Status;

        var ranking = remote.Value?.Ranking;
        var ready = remote.Status == LoadStatus.Ready && ranking is not null && !App.ProfileIsApproximate;
        var tierColor = Theme.WithAlpha(Theme.TierColor(ranking?.Tier ?? 0), 255);

        (string caption, string value, double? number, Func<double, string>? format, Brush color)[] cells =
        [
            ("Score classé", ready ? Fmt.Number(ranking!.Top10Total) : "—", ranking?.Top10Total, v => Fmt.Number((long)v), Theme.Ink),
            ("Rang", ready ? Tiers.Display(ranking!.Tier) : "—", null, null, ready && ranking!.Tier > 0 ? tierColor : Theme.Ink),
            ("Matchs classés", ready ? ranking!.RankedMatches.ToString() : "—", ranking?.RankedMatches, v => ((int)v).ToString(), Theme.Ink),
            ("Victoires · 10 derniers", ready ? ranking!.Wins.ToString() : "—", ranking?.Wins, v => ((int)v).ToString(), Theme.Ink),
            ("Éliminations · 10 derniers", ready ? ranking!.Kills.ToString() : "—", ranking?.Kills, v => ((int)v).ToString(), Theme.Ink),
        ];

        for (var i = 0; i < cells.Length; i++)
        {
            var (caption, value, number, format, color) = cells[i];
            var cell = new StackPanel { Margin = new Thickness(i == 0 ? 24 : 22, 14, 10, 14) };
            if (remote.Status == LoadStatus.Loading || (remote.Status == LoadStatus.Idle && !App.Preview))
            {
                cell.Children.Add(new Skeleton(64, 24, 6));
            }
            else
            {
                var tb = Ui.Display(value, 28, color, FontWeights.SemiBold);
                cell.Children.Add(tb);
                if (countUp && ready && number is { } n && format is not null && n > 0)
                    Anim.CountUp(tb, n, format, 1100, 120 + i * 90);
            }
            cell.Children.Add(Ui.Text(caption, 12, Theme.InkMute).M(0, 2, 0, 0));
            cell.At(0, i);
            grid.Children.Add(cell);
            if (i > 0)
                grid.Children.Add(Ui.VRule().Align(HorizontalAlignment.Left, VerticalAlignment.Center).M(0, 8, 0, 8).At(0, i));
        }
        _statsCard.Child = grid;

        if (remote.Status == LoadStatus.Failed)
        {
            ToolTipService.SetToolTip(_statsCard, remote.Error?.Message);
        }
    }

    // ================================================================== progression de rang

    private void RebuildRank()
    {
        var remote = App.Profile;
        var root = new Grid { Margin = new Thickness(26, 22, 26, 22) };

        if (remote.Status is LoadStatus.Loading or LoadStatus.Idle && !App.Preview)
        {
            var sk = new StackPanel();
            sk.Children.Add(new Skeleton(180, 26));
            sk.Children.Add(new Skeleton(double.NaN, 56, 12).M(0, 20, 0, 0));
            root.Children.Add(sk);
            _rankCard.Child = root;
            return;
        }
        if (remote.Status == LoadStatus.Failed && remote.Error is { } error)
        {
            _rankCard.Child = ErrorState(error, () => _ = App.LoadProfileAsync());
            return;
        }

        var profile = remote.Value!;
        if (App.ProfileIsApproximate)
        {
            _rankCard.Child = EmptyState(Icons.Shield, "Profil détaillé bientôt disponible",
                "Le serveur ne fournit pas encore les statistiques personnelles au nouveau launcher. Le classement public reste consultable.");
            return;
        }
        var ranking = profile.Ranking;
        var thresholds = profile.Thresholds;
        var unranked = ranking.RankedMatches < thresholds.MinMatch || ranking.Tier == 0;
        var currentTier = unranked ? 0 : ranking.Tier;

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- en-tête : score + prochain palier ----
        var head = Ui.Columns(Ui.Star(), Ui.Auto);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Ui.Mono(Ui.Spaced("Score classé"), 10.5, Theme.InkMute).Align(HorizontalAlignment.Left, VerticalAlignment.Center).M(0, 0, 14, 0));
        left.Children.Add(Ui.Display(Fmt.Number(ranking.Top10Total), 30, Theme.Ink));
        var next = RankMath.Next(ranking.Top10Total, currentTier, thresholds);
        if (!unranked && next is { } nx)
            left.Children.Add(Ui.Text($"  /  {Fmt.Number(thresholds.LineFor(nx.Tier))} pour {nx.Name}", 13.5, Theme.InkMute).M(6, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).M(0, 0, 0, 5));
        left.At(0, 0);
        head.Children.Add(left);
        if (profile.Season.Length > 0)
            head.Children.Add(Chip("Saison  " + profile.Season, Theme.InkDim).At(0, 1));
        head.At(0);
        root.Children.Add(head);

        // ---- piste des 6 paliers ----
        var track = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        for (var i = 0; i < 6; i++) track.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
        var bar = new ProgressBarFx { Height = 6, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 31, 0, 0) };
        track.Children.Add(bar);
        track.SizeChanged += (_, _) =>
        {
            var inset = track.ActualWidth / 12;
            bar.Margin = new Thickness(inset, 31, inset, 0);
        };
        Grid.SetColumnSpan(bar, 6);
        var fraction = RankMath.TrackFraction(ranking.Top10Total, ranking.RankedMatches, thresholds);
        bar.Loaded += (_, _) => bar.SetFraction(fraction, Anim.Enabled ? 1400 : 0);

        for (var tier = 1; tier <= 6; tier++)
        {
            var achieved = !unranked && tier <= currentTier;
            var current = !unranked && tier == currentTier;
            var line = thresholds.LineFor(tier);
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            cell.Children.Add(new TierBadge(tier, 54, achieved, current, Loops));
            cell.Children.Add(Ui.Text(Tiers.Display(tier), 13, current ? Theme.Ink : achieved ? Theme.InkDim : Theme.InkMute, current ? FontWeights.Bold : FontWeights.SemiBold, align: TextAlignment.Center).M(0, 8, 0, 0).Align(HorizontalAlignment.Center));
            cell.Children.Add(Ui.Mono(tier == 1 ? "Départ" : Fmt.Number(line), 10.5, Theme.InkMute).M(0, 2, 0, 0).Align(HorizontalAlignment.Center));
            cell.At(0, tier - 1);
            track.Children.Add(cell);
        }
        track.At(1);
        root.Children.Add(track);

        // ---- pied : message d'état ----
        var foot = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        if (unranked)
        {
            var remaining = Math.Max(0, thresholds.MinMatch - ranking.RankedMatches);
            var row = Ui.Columns(Ui.Auto, Ui.Star());
            row.Children.Add(Ui.Text(
                ranking.RankedMatches == 0
                    ? $"Jouez {thresholds.MinMatch} matchs classés pour obtenir votre premier rang."
                    : $"Encore {remaining} match{(remaining > 1 ? "s" : "")} classé{(remaining > 1 ? "s" : "")} pour obtenir votre rang  ({ranking.RankedMatches}/{thresholds.MinMatch}).",
                13, Theme.InkDim).At(0, 0));
            foot.Children.Add(row);
            var matches = new ProgressBarFx { Height = 5, Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            matches.Loaded += (_, _) => matches.SetFraction((double)ranking.RankedMatches / Math.Max(1, thresholds.MinMatch), Anim.Enabled ? 900 : 0);
            foot.Children.Add(matches);
        }
        else if (next is { } n)
        {
            foot.Children.Add(Ui.Text($"Prochain palier : {n.Name} — encore {Fmt.Number(n.Missing)} points.", 13, Theme.InkDim));
        }
        else
        {
            foot.Children.Add(Ui.Text("Palier maximal atteint. Vous régnez sur le classement.", 13, Theme.Gold, FontWeights.SemiBold));
        }
        foot.At(2);
        root.Children.Add(foot);

        _rankCard.Child = root;
    }

    // ================================================================== actualités

    private void RebuildNews()
    {
        var remote = App.Notes;
        var host = new StackPanel();

        if (remote.Status is LoadStatus.Loading or LoadStatus.Idle && !App.Preview)
        {
            for (var i = 0; i < 3; i++)
            {
                var row = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };
                row.Children.Add(new Skeleton(240, 14));
                row.Children.Add(new Skeleton(380, 11).M(0, 8, 0, 0));
                host.Children.Add(row);
                if (i < 2) host.Children.Add(Ui.Rule());
            }
        }
        else if (remote.Status == LoadStatus.Failed && remote.Error is { } error)
        {
            host.Children.Add(ErrorState(error, () => _ = App.LoadNotesAsync()));
        }
        else if (remote.Value is { Count: > 0 } notes)
        {
            var shown = notes.Take(4).ToList();
            for (var i = 0; i < shown.Count; i++)
            {
                host.Children.Add(NewsRow(shown[i]));
                if (i < shown.Count - 1) host.Children.Add(Ui.Rule(Theme.Line));
            }
        }
        else
        {
            host.Children.Add(EmptyState(Icons.Bell, "Rien de neuf pour l'instant", "Les annonces et notes de patch apparaîtront ici."));
        }
        _newsCard.Child = host;
    }

    private FrameworkElement NewsRow(PatchNote note)
    {
        var chevron = Icons.Make("M9 6l6 6-6 6", Theme.InkMute, 18, 2);
        var grid = Ui.Columns(Ui.Star(), Ui.Auto, Ui.Px(28));
        grid.Margin = new Thickness(24, 14, 18, 14);
        var texts = new StackPanel();
        texts.Children.Add(App.Preview ? Ui.Text(note.Title,14,Theme.Ink,FontWeights.SemiBold) : Ui.Raw(note.Title, 14, Theme.Ink, FontWeights.SemiBold));
        if (note.Summary.Length > 0) texts.Children.Add((App.Preview ? Ui.Text(note.Summary,12.5,Theme.InkDim) : Ui.Raw(note.Summary, 12.5, Theme.InkDim)).M(0, 3, 0, 0));
        texts.At(0, 0);
        grid.Children.Add(texts);
        grid.Children.Add(Ui.Text(Fmt.Relative(note.PublishedAt), 12, Theme.InkMute).M(14, 0, 0, 0).At(0, 1));
        chevron.At(0, 2).Align(HorizontalAlignment.Right);
        grid.Children.Add(chevron);

        var hover = new SolidColorBrush(Colors.Transparent);
        var row = new Border { Background = hover, Cursor = Cursors.Hand, Child = grid };
        row.MouseEnter += (_, _) => { Anim.ColorTo(hover, Color.FromArgb(14, 255, 255, 255), 120); chevron.Color = Theme.EmberHi; };
        row.MouseLeave += (_, _) => { Anim.ColorTo(hover, Colors.Transparent, 160); chevron.Color = Theme.InkMute; };
        row.MouseLeftButtonUp += (_, _) => Nav.OpenNote(note);
        return row;
    }
}
