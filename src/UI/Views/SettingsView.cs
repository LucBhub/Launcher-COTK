using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Paramètres : compte, jeu, affichage, support, à propos.</summary>
internal sealed class SettingsView : View
{
    private readonly StackPanel _stack = new() { Margin = new Thickness(28, 6, 28, 34), MaxWidth = 860 };
    private readonly List<FrameworkElement> _reveals = new();

    public SettingsView(LauncherApp app, INavigator nav) : base(app, nav)
    {
        Children.Add(Scroller(_stack));
        Build();
    }

    public override string Title => "Paramètres";

    public override void Activated()
    {
        base.Activated();
        Build(); // relit l'état réel (dossier, session…) à chaque ouverture
        Anim.Reveal(_reveals, 0, 70, 14);
    }

    protected override void Changed(string what)
    {
        if (what is "session" or "client" or "staffconsole" or "launcherupdate") Build();
    }

    private void Build()
    {
        _stack.Children.Clear();
        _reveals.Clear();
        Section("Compte", AccountCard());
        Section("Jeu", GameCard());
        Section("Affichage", DisplayCard());
        Section("Langue", Group(Row("Langue du launcher", "Changement immédiat, choix mémorisé sur cet ordinateur.", new LanguagePicker())));
        if (App.Session?.Account.IsStaff == true) Section("Staff", StaffCard());
        Section("Support", SupportCard());
        Section("À propos", AboutCard());
    }

    private void Section(string title, FrameworkElement card)
    {
        var heading = SectionTitle(title).M(2, _stack.Children.Count == 0 ? 0 : 24, 0, 10);
        _stack.Children.Add(heading);
        _stack.Children.Add(card);
        _reveals.Add(heading);
        _reveals.Add(card);
    }

    // ------------------------------------------------------------------ lignes

    private static Grid Row(string title, string? description, FrameworkElement control)
    {
        var grid = Ui.Columns(Ui.Star(), Ui.Auto);
        grid.Margin = new Thickness(24, 16, 24, 16);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        text.Children.Add(Ui.Text(title, 14, Theme.Ink, FontWeights.SemiBold));
        if (description is not null) text.Children.Add(Ui.Text(description, 12.5, Theme.InkDim, wrap: true).M(0, 3, 0, 0));
        text.At(0, 0);
        control.At(0, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private static Card Group(params FrameworkElement[] rows)
    {
        var card = new Card(14);
        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            stack.Children.Add(rows[i]);
            if (i < rows.Length - 1) stack.Children.Add(Ui.Rule());
        }
        card.Child = stack;
        return card;
    }

    // ------------------------------------------------------------------ cartes

    private FrameworkElement AccountCard()
    {
        var account = App.Session?.Account;
        var initial = string.IsNullOrEmpty(account?.Username) ? "?" : account!.Username[..1].ToUpperInvariant();
        var avatar = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Theme.EmberGradient,
            Child = Ui.Display(initial, 22, Brushes.White).Align(HorizontalAlignment.Center),
        };

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(account is null ? Ui.Text("Déconnecté",16,Theme.Ink,FontWeights.Bold) : Ui.Raw(account.Username, 16, Theme.Ink, FontWeights.Bold));
        if (account is { IsStaff: true })
            nameRow.Children.Add(Chip(account.IsAdmin ? "ADMIN" : "MODÉRATEUR", Theme.Gold, Theme.WithAlpha(Theme.GoldColor, 26), Theme.WithAlpha(Theme.GoldColor, 80), 10.5).M(10, 0, 0, 0));
        who.Children.Add(nameRow);
        who.Children.Add(Ui.Text(account is null ? "" : "Session protégée par le Gestionnaire d'identification Windows", 12.5, Theme.InkDim).M(0, 3, 0, 0));
        if (account is { IsStaff: true })
            who.Children.Add(Ui.Text("Les sessions staff ne sont jamais mémorisées sur ce PC.", 12, Theme.InkMute).M(0, 2, 0, 0));

        var logout = new FxButton("Se déconnecter", ButtonKind.Danger, Icons.Logout, 38, 13);
        logout.Click += async (_, _) => await App.LogoutAsync();

        var grid = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto);
        grid.Margin = new Thickness(24, 18, 24, 18);
        avatar.At(0, 0);
        who.At(0, 1);
        logout.At(0, 2);
        grid.Children.Add(avatar);
        grid.Children.Add(who);
        grid.Children.Add(logout);
        return Group(grid);
    }

    private FrameworkElement GameCard()
    {
        var dir = ClientInstaller.ClientDir;
        var change = new FxButton("Changer…", ButtonKind.Secondary, Icons.Folder, 36, 12.5);
        change.IsEnabled = !App.IsBusy;
        change.Click += async (_, _) => { await App.ChangeClientFolderAsync(); Build(); };
        var open = new FxButton("Ouvrir", ButtonKind.Ghost, null, 36, 12.5);
        open.Click += (_, _) => App.OpenFolder(dir);
        var folderControls = Ui.HStack(open, change.M(6, 0, 0, 0));

        var minimize = new FxToggle(Settings.MinimizeOnPlay);
        minimize.Toggled += on => Settings.MinimizeOnPlay = on;

        var folderRow = Row("Dossier du jeu", dir, folderControls);
        return Group(
            folderRow,
            Row("Réduire le launcher pendant la partie", "Le launcher se met dans la barre des tâches quand le jeu démarre et revient à la fermeture.", minimize));
    }

    private FrameworkElement DisplayCard()
    {
        var reduce = new FxToggle(Settings.ReduceMotion);
        reduce.Toggled += on =>
        {
            Settings.ReduceMotion = on;
            Ticker.Refresh();
            ((MainWindow)Application.Current.MainWindow!).ApplyMotionSetting();
        };
        return Group(Row("Réduire les animations",
            "Désactive braises, parallaxe et transitions. Recommandé sur un PC modeste ; suit le réglage de Windows par défaut.", reduce));
    }

    private FrameworkElement StaffCard()
    {
        var status = App.StaffConsoleStatus;
        var busy = status is StaffConsoleStatus.Installing or StaffConsoleStatus.Checking || App.Op is Operation.Launching or Operation.Playing;
        var (label, kind) = status switch
        {
            StaffConsoleStatus.NeedsInstall => ("Installer la console F8", ButtonKind.Primary),
            StaffConsoleStatus.NeedsUpdate => ("Mettre à jour la console F8", ButtonKind.Primary),
            StaffConsoleStatus.Installing => ("Installation…", ButtonKind.Secondary),
            StaffConsoleStatus.Checking => ("Vérification…", ButtonKind.Secondary),
            StaffConsoleStatus.Unavailable => ("Réessayer", ButtonKind.Secondary),
            _ => ("Réinstaller", ButtonKind.Ghost),
        };
        var action = new FxButton(label, kind, status == StaffConsoleStatus.Unavailable ? null : Icons.Download, 36, 12.5);
        action.IsEnabled = !busy;
        action.Click += async (_, _) =>
        {
            if (App.StaffConsoleStatus == StaffConsoleStatus.Unavailable) await App.RefreshStaffConsoleAsync();
            else await App.InstallStaffConsoleAsync();
        };

        var description = status switch
        {
            StaffConsoleStatus.UpToDate => "Installée et à jour. Elle s'ouvre en jeu avec la touche F8.",
            StaffConsoleStatus.NeedsInstall => "Non installée. Téléchargement depuis l'API réservée au staff, vérifié par SHA-256.",
            StaffConsoleStatus.NeedsUpdate => "Une version plus récente est disponible.",
            StaffConsoleStatus.Installing => "Téléchargement et vérification en cours…",
            StaffConsoleStatus.Checking => "Vérification de la version disponible…",
            StaffConsoleStatus.Unavailable => "Console indisponible sur le serveur pour le moment.",
            _ => "Console F8 réservée au staff.",
        };
        return Group(Row("Console F8 staff", description, action));
    }

    private FrameworkElement SupportCard()
    {
        var export = new FxButton("Exporter le rapport", ButtonKind.Secondary, Icons.Folder, 36, 12.5);
        export.Click += (_, _) => App.ExportDiagnostic();
        var logs = new FxButton("Ouvrir les journaux", ButtonKind.Ghost, null, 36, 12.5);
        logs.Click += (_, _) => App.OpenFolder(AppPaths.LogDir);
        var verbose = new FxToggle(Settings.VerboseLogs);
        verbose.Toggled += on => Settings.VerboseLogs = on;
        var site = new FxButton("cotk.fr", ButtonKind.Ghost, Icons.External, 36, 12.5);
        site.Click += (_, _) => App.OpenUrl(Config.SiteUrl);

        var submit = new FxButton("Choisir un extrait…", ButtonKind.Secondary, Icons.External, 36, 12.5);
        submit.Click += (_,_) => ((MainWindow)Application.Current.MainWindow!).OpenSupportReport();
        var rows = new List<FrameworkElement>
        {
            Row("Envoyer des logs au support", "Envoi volontaire, aperçu expurgé, numéro de suivi et conservation de sept jours.",submit),
            Row("Rapport de diagnostic", "Un fichier .zip (versions, journaux du launcher et du client, sans mot de passe ni ticket) à envoyer au support.",
                Ui.HStack(logs, export.M(6, 0, 0, 0))),
            Row("Journaux détaillés", "À activer seulement si le support vous le demande.", verbose),
            Row("Site et compte", "Créer un compte, réinitialiser un mot de passe, boutique.", site)
        };
        if(App.Session?.Account.IsStaff==true)
        {
            var received=new FxButton("Lire les rapports",ButtonKind.Secondary,Icons.Folder,36,12.5);
            received.Click+=(_,_)=>((MainWindow)Application.Current.MainWindow!).OpenReceivedReports();
            rows.Add(Row("Rapports reçus · staff", "Extraits reçus sur la production, accessibles seulement aux membres du staff.",received));
        }
        return Group(rows.ToArray());
    }

    private FrameworkElement AboutCard()
    {
        var body = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        var head = Ui.HStack(Icons.Make(Icons.Crown, Theme.EmberHi, 26, 1.4).M(0, 0, 12, 0), Ui.Display("COTK — Crown of the King", 20, Theme.Ink));
        body.Children.Add(head);
        body.Children.Add(Ui.Text($"Launcher {Config.Version}  ·  client {App.InstalledVersion ?? "non installé"}  ·  API {(Config.IsLocalApi ? "locale" : "api.cotk.fr")}", 12.5, Theme.InkDim, font: Theme.Mono).M(0, 10, 0, 0));
        if (App.LauncherUpdate is { } update)
        {
            var go = new FxButton(App.LauncherUpdating ? "Installation…" : "Mettre à jour", ButtonKind.Primary, Icons.Download, 36, 12.5);
            go.IsEnabled = !App.LauncherUpdating && !App.IsBusy;
            go.Click += async (_, _) => await App.UpdateLauncherAsync();
            var line = Ui.Columns(Ui.Star(), Ui.Auto);
            var label = Ui.Text($"Mise à jour du launcher disponible : {update.Version}", 13, Theme.Gold, FontWeights.SemiBold);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.At(0, 0);
            go.At(0, 1);
            line.Children.Add(label);
            line.Children.Add(go);
            line.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(line);
        }
        body.Children.Add(Ui.Text("Projet communautaire à but éducatif, non affilié à Daybreak Game Company, Z1 Games ni aucun détenteur des droits sur H1Z1. Aucun asset du jeu n'est distribué avec le launcher.", 12, Theme.InkMute, wrap: true).M(0, 10, 0, 0));
        return Group(body);
    }
}
