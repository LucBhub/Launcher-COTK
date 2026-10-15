using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Bibliothèque : état du client, installation, vérification, réparation, dossier, espace disque.</summary>
internal sealed class LibraryView : View
{
    private readonly Card _gameCard = new(16);
    private readonly Card _transferCard = new(14);
    private readonly TransferPanel _transfer;
    private readonly Card _folderCard = new(14);
    private readonly Card _diskCard = new(14);
    private readonly Border _statusChip = new();
    private readonly TextBlock _statusText = Ui.Text("", 12, Theme.InkDim, FontWeights.SemiBold);
    private readonly Border _statusDot = Ui.Dot(Theme.InkMute, 8);
    private readonly TextBlock _installed = Ui.Text("", 14, Theme.Ink, FontWeights.SemiBold);
    private readonly TextBlock _published = Ui.Text("", 14, Theme.Ink, FontWeights.SemiBold);
    private readonly TextBlock _size = Ui.Text("", 14, Theme.Ink, FontWeights.SemiBold);
    private readonly FxButton _primary = new("Jouer", ButtonKind.Primary, Icons.Play, 48, 15);
    private readonly FxButton _verify = new("Vérifier les fichiers", ButtonKind.Secondary, Icons.Check, 40, 13);
    private readonly FxButton _repair = new("Réparer", ButtonKind.Secondary, Icons.Wrench, 40, 13);
    private readonly FxButton _openFolder = new("Ouvrir le dossier", ButtonKind.Ghost, Icons.Folder, 40, 13);
    private readonly FxButton _uninstall = new("Désinstaller le client", ButtonKind.Danger, Icons.Close, 40, 13);
    private readonly List<FrameworkElement> _reveals = new();
    private bool _transferShown;

    public LibraryView(LauncherApp app, INavigator nav) : base(app, nav)
    {
        _transfer = new TransferPanel(() => App.CancelOperation(), glass: false,App.ToggleDownloadPause,()=>App.DownloadPaused);
        _transferCard.Child = _transfer;
        _transferCard.Visibility = Visibility.Collapsed;
        _transferCard.BorderBrush = Theme.WithAlpha(Theme.EmberColor, 80);

        var stack = new StackPanel { Margin = new Thickness(28, 6, 28, 34) };
        BuildGameCard();
        stack.Children.Add(_gameCard);
        stack.Children.Add(_transferCard.M(0, 16, 0, 0));
        var grid = Ui.Columns(Ui.Star(), Ui.Star());
        _folderCard.Margin = new Thickness(0, 0, 8, 0);
        _diskCard.Margin = new Thickness(8, 0, 0, 0);
        _folderCard.At(0, 0);
        _diskCard.At(0, 1);
        grid.Children.Add(_folderCard);
        grid.Children.Add(_diskCard);
        stack.Children.Add(grid.M(0, 16, 0, 0));
        Children.Add(Scroller(stack));

        _reveals.AddRange([_gameCard, grid]);
        Refresh();
    }

    public override string Title => "Bibliothèque";

    public override void Activated()
    {
        base.Activated();
        Anim.Reveal(_reveals, 0, 80, 16);
        Refresh();
    }

    protected override void Changed(string what)
    {
        if (what == "transfer") { _transfer.Update(App.Transfer, App.Op); return; }
        if (what is "client" or "operation" or "transfer" or "session" or "play") Refresh();
    }

    // ================================================================== carte du jeu

    private void BuildGameCard()
    {
        // ---- pochette : couronne sur fond braise ----
        var cover = new Border { Width = 200, Background = Theme.Bg, CornerRadius = new CornerRadius(15, 0, 0, 15), ClipToBounds = true };
        var art = new Grid();
        art.Children.Add(new Border { Background = Theme.Radial(Color.FromArgb(150, 255, 106, 43), Color.FromArgb(0, 255, 106, 43), 0.5, 0.62, 0.7) });
        art.Children.Add(new Embers(16, 0.7, 21));
        var crown = Icons.Make(Icons.Crown, Theme.WithAlpha(Theme.EmberHiColor, 255), 84, 1.3);
        crown.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.EmberColor, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.75 };
        crown.Align(HorizontalAlignment.Center);
        art.Children.Add(crown);
        cover.Child = art;
        Anim.Pulse(crown, 0.82, 1, 3200, Loops);

        // ---- infos ----
        var info = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        var top = Ui.Columns(Ui.Star(), Ui.Auto);
        var names = new StackPanel();
        names.Children.Add(Ui.Mono(Ui.Spaced("Client du jeu"), 10.5, Theme.EmberHi));
        names.Children.Add(Ui.Display("H1Z1 : King of the Kill", 28, Theme.Ink).M(0, 4, 0, 0));
        names.Children.Add(Ui.Text("Pré-saison 3 · client PS3 authentique", 13, Theme.InkDim).M(0, 2, 0, 0));
        names.At(0, 0);
        top.Children.Add(names);

        _statusChip.CornerRadius = new CornerRadius(8);
        _statusChip.Padding = new Thickness(11, 5, 12, 6);
        _statusChip.VerticalAlignment = VerticalAlignment.Top;
        _statusChip.BorderThickness = new Thickness(1);
        _statusChip.Child = Ui.HStack(_statusDot.M(0, 0, 8, 0), _statusText);
        _statusChip.At(0, 1);
        top.Children.Add(_statusChip);
        info.Children.Add(top);

        var facts = Ui.Columns(Ui.Star(), Ui.Star(), Ui.Star());
        facts.Margin = new Thickness(0, 22, 0, 0);
        facts.Children.Add(Fact("Version installée", _installed).At(0, 0));
        facts.Children.Add(Fact("Version publiée", _published).At(0, 1));
        facts.Children.Add(Fact("Téléchargement", _size).At(0, 2));
        info.Children.Add(facts);

        var actions = new WrapPanel { Margin = new Thickness(0, 20, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _primary.MinWidth = 190;
        _primary.Click += (_, _) => OnPrimary();
        _verify.Click += (_, _) => _ = App.VerifyAsync(repair: false);
        _repair.Click += (_, _) => _ = ConfirmRepair();
        _openFolder.Click += (_, _) => App.OpenFolder(ClientInstaller.ClientDir);
        _uninstall.Click += (_,_) => _ = App.UninstallClientAsync();
        actions.Children.Add(_primary.M(0, 4, 10, 4));
        actions.Children.Add(_verify.M(0, 8, 8, 8));
        actions.Children.Add(_repair.M(0, 8, 8, 8));
        actions.Children.Add(_openFolder.M(0, 8, 0, 8));
        actions.Children.Add(_uninstall.M(0,8,0,8));
        info.Children.Add(actions);

        var row = Ui.Columns(Ui.Px(200), Ui.Star());
        cover.At(0, 0);
        info.At(0, 1);
        row.Children.Add(cover);
        row.Children.Add(info);
        _gameCard.Child = row;
        _gameCard.ClipToBounds = false;
    }

    private static FrameworkElement Fact(string caption, TextBlock value)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(caption, 11.5, Theme.InkMute));
        panel.Children.Add(value.M(0, 4, 0, 0));
        return panel;
    }

    private void OnPrimary()
    {
        switch (App.ClientState)
        {
            case ClientState.Ready: _ = App.PlayAsync(); break;
            case ClientState.Unknown: _ = App.LoadClientStateAsync(); break;
            default: _ = App.InstallOrUpdateAsync(); break;
        }
    }

    private async Task ConfirmRepair()
    {
        var ok = await Nav.Confirm("Réparer le client ?",
            "Tous les fichiers du jeu seront relus et comparés au serveur ; ceux qui sont corrompus ou manquants seront retéléchargés. Vos réglages et touches sont conservés. Cela peut prendre plusieurs minutes.",
            "Lancer la réparation", icon: Icons.Wrench);
        if (ok) await App.VerifyAsync(repair: true);
    }

    // ================================================================== rafraîchissement

    private void Refresh()
    {
        var busy = App.IsBusy;
        var state = App.ClientState;

        var (text, color) = state switch
        {
            ClientState.Ready => ("À jour", Theme.OkColor),
            ClientState.UpdateAvailable => ("Mise à jour disponible", Theme.GoldColor),
            ClientState.NotInstalled => ("Non installé", Theme.InkDimColor),
            ClientState.Checking => ("Vérification…", Theme.InkDimColor),
            _ => ("Indisponible", Theme.ErrColor),
        };
        _statusText.Text = text;
        _statusText.Foreground = Theme.WithAlpha(color, 255);
        _statusDot.Background = Theme.WithAlpha(color, 255);
        _statusChip.Background = Theme.WithAlpha(color, 24);
        _statusChip.BorderBrush = Theme.WithAlpha(color, 70);

        _installed.Text = App.InstalledVersion ?? "—";
        _published.Text = App.LatestVersion ?? "—";
        _size.Text = App.ClientDownloadSize > 0 ? Fmt.Size(App.ClientDownloadSize) : "—";

        switch (state)
        {
            case ClientState.Ready: _primary.Text = "Jouer"; _primary.SetIcon(Icons.Play); break;
            case ClientState.UpdateAvailable: _primary.Text = "Mettre à jour"; _primary.SetIcon(Icons.Download); break;
            case ClientState.NotInstalled: _primary.Text = "Installer le jeu"; _primary.SetIcon(Icons.Download); break;
            case ClientState.Checking: _primary.Text = "Vérification…"; break;
            default: _primary.Text = "Réessayer"; _primary.SetIcon(Icons.Refresh); break;
        }
        _primary.IsEnabled = !busy && state != ClientState.Checking;
        var installed = state is ClientState.Ready or ClientState.UpdateAvailable;
        _verify.IsEnabled = !busy && installed;
        _repair.IsEnabled = !busy && installed;
        _openFolder.IsEnabled = true;
        _uninstall.Visibility=App.CanUninstall ? Visibility.Visible : Visibility.Collapsed;
        _uninstall.IsEnabled=!busy;

        var transfer = App.Op is Operation.Installing or Operation.Verifying or Operation.Repairing or Operation.Uninstalling;
        if (transfer)
        {
            _transfer.Update(App.Transfer, App.Op);
            if (!_transferShown)
            {
                _transferShown = true;
                _transfer.Reset();
                _transferCard.Visibility = Visibility.Visible;
                Anim.Rise(_transferCard, 0, 12, 380);
            }
        }
        else if (_transferShown)
        {
            _transferShown = false;
            _transferCard.Visibility = Visibility.Collapsed;
        }

        RebuildFolder();
        RebuildDisk();
    }

    private void RebuildFolder()
    {
        var path = ClientInstaller.ClientDir;
        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        body.Children.Add(Ui.Text("Dossier d'installation", 12, Theme.InkMute));
        body.Children.Add(Ui.Text(path, 13, Theme.Ink, FontWeights.SemiBold, Theme.Mono).M(0, 6, 0, 0));
        var change = new FxButton("Changer…", ButtonKind.Secondary, Icons.Folder, 36, 12.5) { HorizontalAlignment = HorizontalAlignment.Left };
        change.IsEnabled = !App.IsBusy;
        change.Click += async (_, _) => { await App.ChangeClientFolderAsync(); Refresh(); };
        body.Children.Add(change.M(0, 14, 0, 0));
        _folderCard.Child = body;
    }

    private void RebuildDisk()
    {
        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        body.Children.Add(Ui.Text("Espace disque", 12, Theme.InkMute));
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(ClientInstaller.ClientDir)) ?? "C:\\";
            var drive = new DriveInfo(root);
            var used = 1 - drive.AvailableFreeSpace / (double)drive.TotalSize;
            body.Children.Add(Ui.HStack(
                Ui.Display(Fmt.Size(drive.AvailableFreeSpace), 24, Theme.Ink),
                Ui.Text($"  libres sur {drive.Name.TrimEnd('\\')}", 13, Theme.InkDim).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).M(0, 0, 0, 3)).M(0, 6, 0, 0));
            var bar = new ProgressBarFx { Height = 6, Margin = new Thickness(0, 12, 0, 0) };
            bar.Loaded += (_, _) => bar.SetFraction(used, Anim.Enabled ? 900 : 0);
            body.Children.Add(bar);
            var need = App.ClientState == ClientState.NotInstalled ? App.ClientDownloadSize * 2 : 0;
            if (need > 0)
            {
                var ok = drive.AvailableFreeSpace > need + (512L << 20);
                body.Children.Add(Ui.Text(ok
                    ? $"L'installation demande environ {Fmt.Size(need)} le temps de l'extraction."
                    : $"Attention : il faut environ {Fmt.Size(need)} pour installer le jeu.",
                    12, ok ? Theme.InkMute : Theme.Err, wrap: true).M(0, 10, 0, 0));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("espace disque non lisible", ex);
            body.Children.Add(Ui.Text("Information indisponible", 13, Theme.InkDim).M(0, 6, 0, 0));
        }
        _diskCard.Child = body;
    }
}
