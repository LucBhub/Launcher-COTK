using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Historique des derniers matchs classés : place, score (barre relative), éliminations.</summary>
internal sealed class MatchesView : View
{
    private readonly StackPanel _stack = new() { Margin = new Thickness(28, 6, 28, 34) };
    private readonly List<FrameworkElement> _rows = new();

    public MatchesView(LauncherApp app, INavigator nav) : base(app, nav)
    {
        Children.Add(Scroller(_stack));
        Rebuild();
    }

    public override string Title => "Matchs";

    public override void Activated()
    {
        base.Activated();
        Anim.Reveal(_rows, 0, 45, 12);
    }

    protected override void Changed(string what)
    {
        if (what == "profile") Rebuild();
    }

    private void Rebuild()
    {
        _stack.Children.Clear();
        _rows.Clear();
        var remote = App.Profile;

        if (remote.Status is LoadStatus.Loading or LoadStatus.Idle && !App.Preview)
        {
            var card = new Card(14);
            var list = new StackPanel();
            for (var i = 0; i < 6; i++)
            {
                var row = Ui.Columns(Ui.Px(150), Ui.Px(90), Ui.Star(), Ui.Px(100));
                row.Margin = new Thickness(24, 16, 24, 16);
                row.Children.Add(new Skeleton(90, 14).At(0, 0).Align(HorizontalAlignment.Left));
                row.Children.Add(new Skeleton(44, 26, 8).At(0, 1).Align(HorizontalAlignment.Left));
                row.Children.Add(new Skeleton(240, 10).At(0, 2).Align(HorizontalAlignment.Left));
                list.Children.Add(row);
            }
            card.Child = list;
            _stack.Children.Add(card);
            return;
        }

        if (remote.Status == LoadStatus.Failed && remote.Error is { } error)
        {
            _stack.Children.Add(new Card(14) { Child = ErrorState(error, () => _ = App.LoadProfileAsync()) });
            return;
        }

        var recent = remote.Value?.Ranking.Recent ?? new List<RankMatch>();
        if (App.ProfileIsApproximate)
        {
            _stack.Children.Add(new Card(14) { Child = EmptyState(Icons.Matches, "Historique indisponible",
                "Le backend ne fournit pas encore le profil détaillé au nouveau launcher. Aucun résultat personnel n'est inventé.") });
            return;
        }
        if (recent.Count == 0)
        {
            var go = new FxButton("Retour à l'accueil", ButtonKind.Secondary, Icons.Home, 38, 13);
            go.Click += (_, _) => Nav.Go(Page.Home);
            _stack.Children.Add(new Card(14) { Child = EmptyState(Icons.Matches, "Aucun match classé cette saison",
                "Lancez une partie : vos placements, scores et éliminations apparaîtront ici.", go) });
            return;
        }

        // ---- résumé ----
        var best = recent.MaxBy(m => m.Score)!;
        var top = recent.Min(m => m.Placement);
        var kills = recent.Sum(m => m.Kills);
        var wins = recent.Count(m => m.Placement == 1);
        var summary = Ui.Columns(Ui.Star(), Ui.Star(), Ui.Star(), Ui.Star());
        (string label, string value, Brush color)[] tiles =
        [
            ("Meilleur score", Fmt.Number(best.Score), Theme.Ink),
            ("Meilleure place", $"#{top}", PlacementColor(top)),
            ("Victoires", wins.ToString(), wins > 0 ? Theme.Gold : Theme.Ink),
            ("Éliminations", kills.ToString(), Theme.Ink),
        ];
        for (var i = 0; i < tiles.Length; i++)
        {
            var (label, value, color) = tiles[i];
            var tile = new Card(14) { Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 3 ? 0 : 6, 0), Padding = new Thickness(20, 14, 20, 14) };
            tile.Child = Ui.VStack(Ui.Display(value, 28, color), Ui.Text(label, 12, Theme.InkMute).M(0, 2, 0, 0));
            tile.At(0, i);
            summary.Children.Add(tile);
            _rows.Add(tile);
        }
        _stack.Children.Add(summary);

        // ---- liste ----
        _stack.Children.Add(SectionTitle($"{recent.Count} derniers matchs classés").M(2, 24, 0, 10));
        var card2 = new Card(14);
        var rows = new StackPanel();
        var maxScore = Math.Max(1, recent.Max(m => m.Score));
        for (var i = 0; i < recent.Count; i++)
        {
            var row = MatchRow(recent[i], maxScore);
            rows.Children.Add(row);
            _rows.Add(row);
            if (i < recent.Count - 1) rows.Children.Add(Ui.Rule());
        }
        card2.Child = rows;
        _stack.Children.Add(card2);
    }

    private static Brush PlacementColor(int place) => place switch
    {
        1 => Theme.Gold,
        2 => Theme.WithAlpha(Theme.TierColor(2), 255),
        3 => Theme.WithAlpha(Theme.TierColor(1), 255),
        _ => Theme.Ink,
    };

    private static FrameworkElement MatchRow(RankMatch match, long maxScore)
    {
        var grid = Ui.Columns(Ui.Px(150), Ui.Px(96), Ui.Star(), Ui.Px(96));
        grid.Margin = new Thickness(24, 13, 24, 13);

        grid.Children.Add(Ui.Text(Fmt.Relative(match.EndedAt), 13, Theme.InkDim).At(0, 0));

        var podium = match.Placement <= 3;
        var color = PlacementColor(match.Placement);
        var badge = new Border
        {
            Height = 30, MinWidth = 52, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left,
            Background = podium ? Theme.WithAlpha(((SolidColorBrush)color).Color, 30) : Theme.White08,
            BorderBrush = podium ? Theme.WithAlpha(((SolidColorBrush)color).Color, 90) : Theme.Line, BorderThickness = new Thickness(1),
            Child = Ui.Display($"#{match.Placement}", 15, color).Align(HorizontalAlignment.Center),
        };
        badge.At(0, 1);
        grid.Children.Add(badge);

        var scoreCell = Ui.Columns(Ui.Px(96), Ui.Star());
        scoreCell.Children.Add(Ui.Text(Fmt.Number(match.Score), 14, Theme.Ink, FontWeights.SemiBold).At(0, 0));
        var bar = new ProgressBarFx { Height = 5, Margin = new Thickness(6, 0, 28, 0) };
        bar.Loaded += (_, _) => bar.SetFraction((double)match.Score / maxScore, Anim.Enabled ? 900 : 0);
        bar.At(0, 1);
        scoreCell.Children.Add(bar);
        scoreCell.At(0, 2);
        grid.Children.Add(scoreCell);

        var kills = Ui.HStack(Icons.Make(Icons.Skull, Theme.InkMute, 16, 1.6).M(0, 0, 7, 0), Ui.Text(match.Kills.ToString(), 14, Theme.Ink, FontWeights.SemiBold));
        kills.HorizontalAlignment = HorizontalAlignment.Right;
        kills.At(0, 3);
        grid.Children.Add(kills);
        return grid;
    }
}
