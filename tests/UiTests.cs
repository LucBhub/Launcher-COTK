using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using COTK.Next.Core;
using COTK.Next.UI;
using Page = COTK.Next.UI.Page;

namespace COTK.Next.Tests;

// Exécuté dans une vraie fenêtre WPF, hors de l'exécutable livré.
internal static class UiTests
{
    private static int _exit;
    private static int _passed;

    [STAThread]
    private static int Main()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "cotk-ui-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("COTK_SETTINGS_DIR", Path.Combine(sandbox, "settings"));
        Environment.SetEnvironmentVariable("COTK_LOG_DIR", Path.Combine(sandbox, "logs"));
        Settings.Load();
        L.Initialize();
        Settings.ReduceMotion = false;
        var app = new Application();
        Styles.Install();
        var controller = new LauncherApp(true);
        var window = new MainWindow(controller, null);
        app.MainWindow = window;
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("FAIL UI exception : " + e.Exception); _exit = 1; e.Handled = true; };
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await Task.Delay(1000);
                Check(controller.Session?.Account.Username == "IronWolf_92", "session aperçu initialisée");
                Check(controller.ClientState == ClientState.Ready, "client aperçu prêt");
                var label=Ui.Text("Se connecter");
                var raw=Ui.Raw("Or");
                Check(label.Text=="Sign in","anglais par défaut dans les libellés");
                // Load a real visual so the live language subscription is exercised.
                var labels=new StackPanel { Children={label,raw} };
                var languageHost=Descendants(window).OfType<ModalHost>().Single();
                languageHost.Show(labels);
                await Task.Delay(100);
                L.Select("zh-CN");
                Check(label.Text=="登录" && raw.Text=="Or","changement chinois immédiat, données utilisateur intactes");
                Check(label.FontFamily.Source.Contains("YaHei"),"police chinoise sélectionnée");
                L.Select("fr"); Check(label.Text=="Se connecter","retour au français sans reconstruction");
                L.Select("en");
                languageHost.Hide(immediate:true);
                var picker=Descendants(window).OfType<LanguagePicker>().First();
                var popup=(System.Windows.Controls.Primitives.Popup)typeof(LanguagePicker).GetField("_popup",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(picker)!;
                Descendants(picker).OfType<FxButton>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Check(popup.IsOpen,"sélecteur de langue ouvert via bouton réel");
                var choices=Descendants(popup.Child).OfType<FxButton>().ToList();
                Check(choices.Select(b=>b.Text).SequenceEqual(new[]{"English","Français","简体中文"}),"anglais en premier et noms de langues natifs");
                choices[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(L.Language=="fr" && !popup.IsOpen,"choix du français via le sélecteur");
                L.Select("en");
                foreach (Page page in Enum.GetValues<Page>())
                {
                    window.Go(page);
                    await Task.Delay(160);
                    window.UpdateLayout();
                    Check(window.Title.Contains("COTK"), $"page {page} rendue");
                }

                // Les pages sont conservées. Après warmup, 25 navigations ne doivent pas multiplier les horloges.
                var count = LoopGroup.ClockCount;
                for (var i = 0; i < 5; i++)
                    foreach (Page page in Enum.GetValues<Page>()) { window.Go(page); await Task.Delay(30); }
                Check(LoopGroup.ClockCount <= count + 3, $"horloges stables ({count} → {LoopGroup.ClockCount})");

                window.Activate();
                window.ShowError(Errors.TicketRejected(), null);
                await Task.Delay(200);
                Check(Descendants(window).OfType<ModalHost>().Single().IsOpen, "modale d'erreur ouverte");
                Check(Descendants(window).OfType<ModalHost>().Single().IsKeyboardFocusWithin, "focus clavier isolé dans la modale");
                Descendants(window).OfType<ModalHost>().Single().Hide(immediate: true);

                var confirmation = window.Confirm("Test", "Vérification du dialogue", "Continuer");
                await Task.Delay(80);
                var ok = Descendants(window).OfType<FxButton>().Single(b => b.Text == L.T("CONTINUER"));
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(await confirmation, "confirmation acceptée via bouton réel");

                controller.PreviewSetClient(ClientState.NotInstalled);
                var install = controller.InstallOrUpdateAsync();
                await Task.Delay(600);
                Check(controller.Op == Operation.Installing && controller.Transfer?.BytesDone > 0, "progression affichée");
                controller.ToggleDownloadPause();
                await Task.Delay(200);
                var pausedBytes=controller.Transfer!.BytesDone;
                await Task.Delay(350);
                Check(controller.DownloadPaused && controller.Transfer.Phase==TransferPhase.Paused && controller.Transfer.BytesDone==pausedBytes,"pause réelle de la progression sans annulation");
                controller.ToggleDownloadPause();await Task.Delay(200);
                Check(!controller.DownloadPaused && controller.Transfer!.BytesDone>pausedBytes,"reprise de la progression");
                controller.CancelOperation();
                await install;
                Check(!controller.IsBusy, "annulation restitue les commandes");

                controller.PreviewSetClient(ClientState.Ready);
                window.Go(Page.Library);
                var cancelledUninstall=controller.UninstallClientAsync();
                await Task.Delay(100);
                var modal=Descendants(window).OfType<ModalHost>().Single();
                Check(modal.IsOpen && controller.Op==Operation.Uninstalling,"confirmation de désinstallation ouverte");
                modal.Hide(immediate:true);
                await cancelledUninstall;
                Check(controller.ClientState==ClientState.Ready && !controller.IsBusy,"refus de désinstallation sans effet");
                var uninstall=controller.UninstallClientAsync();
                await Task.Delay(100);
                Descendants(modal).OfType<FxButton>().Single(b=>b.Text==L.T("DÉSINSTALLER LE CLIENT")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await uninstall;
                Check(controller.ClientState==ClientState.NotInstalled && !controller.CanUninstall,"désinstallation simulée, état non installé et bouton masqué");

                window.Go(Page.Settings);
                await Task.Delay(500);
                bool Named(FxButton b,string key)=>string.Equals(b.Text,L.T(key),StringComparison.OrdinalIgnoreCase);
                bool ConsoleButton(string key)=>Descendants(window).OfType<FxButton>().Any(b=>Named(b,key));
                Check(!ConsoleButton("Installer la console F8") && !ConsoleButton("Réinstaller"),"console F8 invisible pour un joueur");
                controller.PreviewSetStaff(StaffConsoleStatus.NeedsInstall);
                await Task.Delay(500);
                Check(ConsoleButton("Installer la console F8"),"carte console F8 visible pour un staff");
                Descendants(window).OfType<FxButton>().Single(b=>Named(b,"Installer la console F8")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(300);
                Check(controller.StaffConsoleStatus==StaffConsoleStatus.Installing && !Descendants(window).OfType<FxButton>().Single(b=>Named(b,"Installation…")).IsEnabled,"installation en cours, bouton désactivé (pas de double clic)");
                await Task.Delay(1500);
                Check(controller.StaffConsoleStatus==StaffConsoleStatus.UpToDate && ConsoleButton("Réinstaller"),"console installée, état à jour affiché");

                window.Go(Page.Home);
                await Task.Delay(650);
                var indicator = Descendants(window).OfType<Border>().Single(b => b.Width == 3 && b.Height == 20);
                Check(indicator.Visibility == Visibility.Visible && indicator.TranslatePoint(new Point(0, 0), window).Y >= 70, "indicateur de navigation correctement positionné");
                Settings.ReduceMotion = true;
                window.ApplyMotionSetting();
                Check(!Anim.Enabled, "réduction des animations immédiate");
                var target = new Border();
                Anim.Fade(target, 0.5);
                Check(target.Opacity == 0.5, "valeur finale correcte sans animation");
                await controller.LogoutAsync();
                await Task.Delay(100);
                Check(controller.Session is null, "déconnexion de l'aperçu sans toucher aux identifiants Windows");
            }
            catch (Exception ex) { _exit = 1; Console.WriteLine("FAIL " + ex); }
            finally
            {
                Console.WriteLine($"UI : {_passed} vérifications réussies, exit={_exit}");
                app.Shutdown();
            }
        };
        app.Run(window);
        return _exit;
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
        _passed++;
        Console.WriteLine("OK " + message);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
