using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Classement de la saison (top 100) : médailles, palier, score, matchs ; votre ligne est mise en avant.</summary>
internal sealed class LeaderboardView : View
{
    private readonly StackPanel _stack = new() { Margin = new Thickness(28, 6, 28, 34) };
    private readonly List<FrameworkElement> _rows = new();

    public LeaderboardView(LauncherApp app, INavigator nav) : base(app, nav)
    {
        Children.Add(Scroller(_stack));
        Rebuild();
    }

    public override string Title => "Classement";

    public override void Activated()
    {
        base.Activated();
        Anim.Reveal(_rows.Take(16), 0, 32, 10);
        if (App.Board.Status is LoadStatus.Idle or LoadStatus.Failed) _ = App.LoadBoardAsync();
    }

    protected override void Changed(string what)
    {
        if (what is "board" or "session") Rebuild();
    }

    private void Rebuild()
    {
        _stack.Children.Clear();
        _rows.Clear();
        var remote = App.Board;

        var head = Ui.Columns(Ui.Star(), Ui.Auto, Ui.Auto);
        head.Children.Add(Ui.Text("Les meilleurs survivants de la saison en cours.", 13.5, Theme.InkDim).At(0, 0));
        if (remote.Value is { Season.Length: > 0 } board) head.Children.Add(Chip("Saison  " + board.Season, Theme.InkDim).M(0, 0, 10, 0).At(0, 1));
        var refresh = new FxButton("Actualiser", ButtonKind.Secondary, Icons.Refresh, 34, 12.5);
        refresh.IsBusy = remote.Status == LoadStatus.Loading;
        refresh.Click += (_, _) => _ = App.LoadBoardAsync();
        refresh.At(0, 2);
        head.Children.Add(refresh);
        _stack.Children.Add(head);

        var card = new Card(14) { Margin = new Thickness(0, 14, 0, 0) };
        _stack.Children.Add(card);

        if (remote.Status is LoadStatus.Loading or LoadStatus.Idle && remote.Value is null)
        {
            var sk = new StackPanel();
            for (var i = 0; i < 9; i++)
            {
                var row = Ui.Columns(Ui.Px(70), Ui.Star(), Ui.Px(150), Ui.Px(130), Ui.Px(90));
                row.Margin = new Thickness(24, 14, 24, 14);
                row.Children.Add(new Skeleton(26, 26, 13).At(0, 0).Align(HorizontalAlignment.Left));
                row.Children.Add(new Skeleton(150 + (i % 3) * 30, 13).At(0, 1).Align(HorizontalAlignment.Left));
                row.Children.Add(new Skeleton(80, 12).At(0, 2).Align(HorizontalAlignment.Left));
                row.Children.Add(new Skeleton(80, 12).At(0, 3).Align(HorizontalAlignment.Left));
                sk.Children.Add(row);
            }
            card.Child = sk;
            return;
        }

        if (remote.Status == LoadStatus.Failed && remote.Value is null && remote.Error is { } error)
        {
            card.Child = ErrorState(error, () => _ = App.LoadBoardAsync());
            return;
        }

        var entries = remote.Value?.Entries ?? new List<LeaderEntry>();
        if (entries.Count == 0)
        {
            card.Child = EmptyState(Icons.Trophy, "Le classement est vide", "Dès qu'un match classé sera terminé, les meilleurs scores apparaîtront ici.");
            return;
        }

        var me = App.Session?.Account.Username;
        var list = new StackPanel();
        list.Children.Add(HeaderRow());
        list.Children.Add(Ui.Rule());
        for (var i = 0; i < entries.Count; i++)
        {
            var row = EntryRow(entries[i], string.Equals(entries[i].Username, me, StringComparison.OrdinalIgnoreCase));
            list.Children.Add(row);
            _rows.Add(row);
            if (i < entries.Count - 1) list.Children.Add(Ui.Rule(Theme.WithAlpha(Theme.LineColor, 150)));
        }
        card.Child = list;
        if (IsCurrent) Anim.Reveal(_rows.Take(16), 0, 28, 8);
    }

    private static FrameworkElement HeaderRow()
    {
        var grid = Ui.Columns(Ui.Px(70), Ui.Star(), Ui.Px(150), Ui.Px(130), Ui.Px(90));
        grid.Margin = new Thickness(24, 12, 24, 12);
        string[] labels = ["RANG", "JOUEUR", "PALIER", "SCORE", "MATCHS"];
        for (var i = 0; i < labels.Length; i++)
        {
            var text = Ui.Mono(labels[i], 10.5, Theme.InkMute);
            text.At(0, i);
            if (i >= 3) text.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(text);
        }
        return grid;
    }

    private static FrameworkElement EntryRow(LeaderEntry entry, bool isMe)
    {
        var grid = Ui.Columns(Ui.Px(70), Ui.Star(), Ui.Px(150), Ui.Px(130), Ui.Px(90));
        grid.Margin = new Thickness(24, 10, 24, 10);

        // rang : médaille pour le podium
        FrameworkElement rank;
        if (entry.Rank <= 3)
        {
            var color = entry.Rank switch { 1 => Theme.GoldColor, 2 => Theme.TierColor(2), _ => Theme.TierColor(1) };
            rank = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Left,
                Background = Theme.WithAlpha(color, 34), BorderBrush = Theme.WithAlpha(color, 130), BorderThickness = new Thickness(1.2),
                Child = Ui.Display(entry.Rank.ToString(), 14, Theme.WithAlpha(color, 255)).Align(HorizontalAlignment.Center),
            };
        }
        else
        {
            rank = Ui.Display(entry.Rank.ToString(), 16, Theme.InkMute, FontWeights.Medium).M(8, 0, 0, 0);
        }
        rank.At(0, 0);
        grid.Children.Add(rank);

        var name = new StackPanel { Orientation = Orientation.Horizontal };
        var display = string.IsNullOrWhiteSpace(entry.CharacterName) ? entry.Username : entry.CharacterName;
        name.Children.Add(Ui.Raw(display, 14, Theme.Ink, isMe ? FontWeights.Bold : FontWeights.SemiBold));
        if (isMe) name.Children.Add(new Border { Background = Theme.WithAlpha(Theme.EmberColor, 36), BorderBrush = Theme.WithAlpha(Theme.EmberColor, 100), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text("VOUS", 10, Theme.EmberHi, FontWeights.Bold) });
        name.At(0, 1);
        grid.Children.Add(name);

        var tier = Ui.HStack(new TierBadge(entry.Tier, 28, true, false).M(0, 0, 10, 0), Ui.Text(Tiers.Display(entry.Tier), 13, Theme.WithAlpha(Theme.TierColor(entry.Tier), 255), FontWeights.SemiBold));
        tier.At(0, 2);
        grid.Children.Add(tier);

        grid.Children.Add(Ui.Display(Fmt.Number(entry.Top10Total), 17, Theme.Ink, FontWeights.Medium).Align(HorizontalAlignment.Right).At(0, 3));
        grid.Children.Add(Ui.Text(entry.RankedMatches.ToString(), 13.5, Theme.InkDim).Align(HorizontalAlignment.Right).At(0, 4));

        var hover = new SolidColorBrush(isMe ? Color.FromArgb(22, 255, 106, 43) : Colors.Transparent);
        var row = new Border { Background = hover, Child = grid };
        row.MouseEnter += (_, _) => Anim.ColorTo(hover, isMe ? Color.FromArgb(40, 255, 106, 43) : Color.FromArgb(12, 255, 255, 255), 120);
        row.MouseLeave += (_, _) => Anim.ColorTo(hover, isMe ? Color.FromArgb(22, 255, 106, 43) : Colors.Transparent, 180);
        return row;
    }
}
