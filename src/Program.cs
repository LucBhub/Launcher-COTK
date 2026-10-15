using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using COTK.Next.Core;
using COTK.Next.UI;

namespace COTK.Next;

internal static class Program
{
    /// <summary>
    /// Arguments (développement) :
    ///   --preview            données fictives, aucun appel réseau, aucun jeu lancé
    ///   --screen=NOM         page ou scénario à ouvrir (library, matches, leaderboard, settings, install, update, installing, playing, error, note, toast)
    ///   --capture=FICHIER    écrit une capture PNG de la fenêtre puis quitte (--capture-delay=ms)
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // Mise à jour du launcher (mêmes arguments que l'ancien launcher, dont les installations migrent vers celui-ci) :
        //   --apply-launcher-update PLAN   copie installatrice qui remplace les fichiers puis relance le launcher
        //   --cleanup-updater EXE          nouveau launcher : supprime la copie installatrice de %TEMP%
        if (args.Length == 2 && string.Equals(args[0], "--cleanup-updater", StringComparison.Ordinal))
        {
            var updater = args[1];
            new Thread(() => LauncherUpdater.Cleanup(updater)) { IsBackground = true, Name = "updater-cleanup" }.Start();
            args = Array.Empty<string>();
        }
        if (args.Length == 2 && string.Equals(args[0], "--apply-launcher-update", StringComparison.Ordinal))
            return LauncherUpdater.Apply(args[1]);

        var preview = args.Contains("--preview") || Environment.GetEnvironmentVariable("COTK_PREVIEW") == "1";
        var screen = Arg(args, "--screen");
        var capture = Arg(args, "--capture");
        var captureDelay = int.TryParse(Arg(args, "--capture-delay"), out var delay) ? delay : 3200;

        // L'aperçu ne peut ni modifier les réglages du joueur ni lire ses identifiants.
        if (preview)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COTK_SETTINGS_DIR")))
                Environment.SetEnvironmentVariable("COTK_SETTINGS_DIR", Path.Combine(Path.GetTempPath(), "cotk-next-preview", "settings"));
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COTK_LOG_DIR")))
                Environment.SetEnvironmentVariable("COTK_LOG_DIR", Path.Combine(Path.GetTempPath(), "cotk-next-preview", "logs"));
        }

        // Une seule instance : une seconde ouverture réveille la première (jamais deux tickets qui s'écrasent).
        var suffix = preview ? ".Preview" : "";
        using var mutex = new Mutex(true, preview ? @"Local\COTK.Launcher.Next.Single.Preview" : @"Local\COTK.Launcher.SingleInstance", out var first);
        if (!first)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(@"Local\COTK.Launcher.Next.Wake" + suffix);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                MessageBox.Show(L.T("Un launcher COTK est déjà ouvert. Fermez l'ancienne version avant d'ouvrir la refonte."), "COTK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return 0;
        }
        using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\COTK.Launcher.Next.Wake" + suffix);

        // 60 images/s maximum quelle que soit la fréquence de l'écran : même rendu, moins de CPU/GPU.
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(60));

        Settings.Load();
        L.Initialize(Arg(args,"--lang"));
        Log.Info($"=== COTK Launcher {Config.Version} démarre (aperçu={preview}) ===");

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Styles.Install();

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("exception non gérée", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("tâche en échec non observée", e.Exception); e.SetObserved(); };

        LauncherApp launcher;
        MainWindow window;
        try
        {
            launcher = new LauncherApp(preview, screen);
            window = new MainWindow(launcher, screen);
        }
        catch (Exception ex)
        {
            var error = Errors.From(ex);
            Log.Error($"démarrage impossible : {error.Code}", ex);
            MessageBox.Show(L.T(error.Report()), L.T("COTK — le launcher ne peut pas démarrer"), MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }

        // Une exception non gérée dans l'interface ne ferme plus le launcher : elle est journalisée et expliquée.
        var reported = 0;
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("exception d'interface non gérée", e.Exception);
            e.Handled = true;
            if (Interlocked.Increment(ref reported) > 3) return; // pas de boucle d'erreurs
            try { window.ShowError(Errors.From(e.Exception), null); } catch { }
        };

        var waiter = new Thread(() =>
        {
            while (true)
            {
                wake.WaitOne();
                window.Dispatcher.BeginInvoke(() =>
                {
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                });
            }
        }) { IsBackground = true, Name = "single-instance-wake" };
        waiter.Start();

        if (capture is not null) ScheduleCapture(window, capture, captureDelay);

        app.MainWindow = window;
        return app.Run(window);
    }

    private static string? Arg(string[] args, string name)
    {
        var prefix = name + "=";
        return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    }

    private static void ScheduleCapture(Window window, string path, int delayMs)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var element = (FrameworkElement)window.Content;
                var dpi = VisualTreeHelper.GetDpi(element);
                var width = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX);
                var height = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
                var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bitmap.Render(element);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using var stream = File.Create(path);
                encoder.Save(stream);
                Log.Info($"capture écrite : {path} ({width}x{height})");
            }
            catch (Exception ex)
            {
                Log.Error("capture impossible", ex);
            }
            window.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        };
        window.Loaded += (_, _) => timer.Start();
    }
}
