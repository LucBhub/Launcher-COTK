using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

/// <summary>Progression d'un transfert : phase, pourcentage, barre, vitesse, temps restant, fichiers.
/// Réutilisée dans la bannière d'accueil et dans la Bibliothèque.</summary>
internal sealed class TransferPanel : Border
{
    private readonly TextBlock _phase = Ui.Mono("", 10.5, Theme.EmberHi);
    private readonly TextBlock _percent = Ui.Display("0 %", 30, Theme.Ink);
    private readonly TextBlock _message = Ui.Text("", 12.5, Theme.InkDim);
    private readonly TextBlock _speed = Ui.Mono("", 11.5, Theme.InkDim);
    private readonly TextBlock _bytes = Ui.Mono("", 11.5, Theme.InkMute);
    private readonly ProgressBarFx _bar = new() { Height = 8 };
    private readonly FxButton _stop = new("Annuler",ButtonKind.Ghost,null,28,12);
    private readonly FxButton _pause=new("Pause",ButtonKind.Ghost,Icons.Pause,28,12);
    private readonly Func<bool>? _paused;

    public TransferPanel(Action cancel, bool glass,Action? pause=null,Func<bool>? paused=null)
    {
        _paused=paused;
        _pause.Click+=(_,_)=>pause?.Invoke();
        _pause.Visibility=Visibility.Collapsed;
        if (glass)
        {
            Background = Theme.WithAlpha(Color.FromRgb(8, 8, 10), 168);
            BorderBrush = Theme.White12;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(14);
        }
        Padding = new Thickness(18, 16, 18, 14);

        var grid = Ui.Rows(Ui.Auto, Ui.Auto, Ui.Auto, Ui.Auto);
        var top = Ui.Columns(Ui.Star(), Ui.Auto);
        _phase.At(0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).M(0, 0, 0, 8);
        _percent.At(0, 1);
        top.Children.Add(_phase);
        top.Children.Add(_percent);
        top.At(0);
        grid.Children.Add(top);

        _bar.M(0, 2, 0, 0).At(1);
        grid.Children.Add(_bar);

        var info = Ui.Columns(Ui.Star(), Ui.Auto);
        _message.At(0, 0);
        _speed.At(0, 1);
        info.Children.Add(_message);
        info.Children.Add(_speed);
        info.M(0, 12, 0, 0).At(2);
        grid.Children.Add(info);

        var foot = Ui.Columns(Ui.Star(), Ui.Auto);
        _bytes.At(0, 0);
        var stop = _stop;
        stop.Click += (_, _) => cancel();
        stop.At(0, 1);
        foot.Children.Add(_bytes);
        var controls=Ui.HStack(_pause,stop);controls.At(0,1);foot.Children.Add(controls);
        foot.M(0, 6, 0, 0).At(3);
        grid.Children.Add(foot);
        Child = grid;
    }

    public void Reset()
    {
        _bar.Reset();
        _percent.Text = "0 %";
        _phase.Text = "";
        _message.Text = "Préparation…";
        _speed.Text = "";
        _bytes.Text = "";
        _bar.Indeterminate = true;
    }

    public void Update(TransferProgress? p, Operation op)
    {
        _pause.Visibility=op==Operation.Installing && p?.Phase is TransferPhase.Downloading or TransferPhase.Paused ? Visibility.Visible : Visibility.Collapsed;
        _pause.Text=_paused?.Invoke()==true ? "Reprendre" : "Pause";
        _stop.Visibility=op==Operation.Uninstalling && p?.Phase==TransferPhase.Uninstalling ? Visibility.Collapsed : Visibility.Visible;
        if (p is null)
        {
            _phase.Text = Ui.Spaced(op switch { Operation.Verifying => "Vérification", Operation.Repairing => "Réparation", Operation.Uninstalling=>"Désinstallation", _ => "Installation" });
            _message.Text = "Préparation…";
            _bar.Indeterminate = true;
            _percent.Text = "";
            return;
        }

        _phase.Text = Ui.Spaced(p.Phase switch
        {
            TransferPhase.Preparing => "Préparation",
            TransferPhase.Analyzing => "Analyse",
            TransferPhase.Downloading => "Téléchargement",
            TransferPhase.Verifying => "Vérification",
            TransferPhase.Extracting => "Extraction",
            TransferPhase.Installing => "Installation",
            TransferPhase.Cleaning => "Nettoyage",
            TransferPhase.Uninstalling=>"Désinstallation",
            TransferPhase.Paused=>"En pause",
            _ => "Terminé",
        });
        _message.Text = p.Message;

        if (p.Determinate)
        {
            _bar.Indeterminate = false;
            _bar.SetFraction(p.Fraction);
            _percent.Text = $"{(int)(p.Fraction * 100)} %";
            _bytes.Text = p.Phase==TransferPhase.Uninstalling ? $"{p.FilesDone}/{p.FilesTotal} fichiers" : p.FilesTotal > 0
                ? $"{Fmt.Size(p.BytesDone)} / {Fmt.Size(p.BytesTotal)} · {p.FilesDone}/{p.FilesTotal} fichiers"
                : $"{Fmt.Size(p.BytesDone)} / {Fmt.Size(p.BytesTotal)}";
        }
        else
        {
            _bar.Indeterminate = true;
            _percent.Text = "";
            _bytes.Text = "";
        }

        _speed.Text = p.Phase == TransferPhase.Downloading && p.BytesPerSecond > 0
            ? $"{Fmt.Speed(p.BytesPerSecond)} · {Fmt.Eta(p.Eta)}"
            : "";
    }
}

/// <summary>Le grand bouton d'action de la bannière : il devient JOUER / INSTALLER / METTRE À JOUR, puis se
/// transforme en panneau de progression pendant un transfert ou en suivi de lancement pendant le démarrage du
/// jeu. Un seul endroit, un seul geste, quel que soit l'état.</summary>
internal sealed class PlayPanel : Grid
{
    private readonly LauncherApp _app;
    private readonly FxButton _cta = new("Jouer", ButtonKind.Primary, Icons.Play, 58, 17);
    private readonly TextBlock _caption = Ui.Text("", 12, Theme.InkDim, align: TextAlignment.Center);
    private readonly Border _dot = Ui.Dot(Theme.InkMute, 7);
    private readonly StackPanel _idle;
    private readonly TransferPanel _transfer;
    private readonly Border _session;
    private readonly TextBlock _sessionText = Ui.Text("", 13.5, Theme.Ink, FontWeights.SemiBold, wrap: true);
    private readonly Border[] _steps = new Border[5];
    private readonly Border _attempt;
    private readonly TextBlock _attemptText = Ui.Text("", 11, Theme.Gold, FontWeights.SemiBold);
    private readonly FxButton _cancelLaunch = new("Annuler", ButtonKind.Ghost, null, 28, 12);
    private readonly Border _liveDot = Ui.Dot(Theme.Ok, 9);
    private FrameworkElement? _shown;

    private static readonly string[] StepNames = ["Serveur", "Session", "Client", "Monde", "En jeu"];

    public PlayPanel(LauncherApp app)
    {
        _app = app;
        Width = 312;

        // ---- repos : bouton + légende de l'état du serveur ----
        _cta.Click += (_, _) => OnCta();
        var captionRow = Ui.HStack(_dot.M(0, 0, 8, 0), _caption);
        captionRow.HorizontalAlignment = HorizontalAlignment.Center;
        _idle = Ui.VStack(_cta, captionRow.M(0, 11, 0, 0));

        // ---- transfert ----
        _transfer = new TransferPanel(() => _app.CancelOperation(), glass: true,_app.ToggleDownloadPause,()=>_app.DownloadPaused);

        // ---- lancement / en jeu ----
        var stepGrid = Ui.Columns(Ui.Star(), Ui.Star(), Ui.Star(), Ui.Star(), Ui.Star());
        for (var i = 0; i < _steps.Length; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0) };
            _steps[i] = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.White12 };
            cell.Children.Add(_steps[i]);
            cell.Children.Add(Ui.Text(StepNames[i], 10, Theme.InkMute).M(0, 6, 0, 0));
            cell.At(0, i);
            stepGrid.Children.Add(cell);
        }
        _attempt = new Border
        {
            Background = Theme.WithAlpha(Theme.GoldColor, 28), BorderBrush = Theme.WithAlpha(Theme.GoldColor, 80), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3), Child = _attemptText, Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0),
        };
        _cancelLaunch.Click += (_, _) => _app.CancelOperation();
        var head = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto);
        _liveDot.M(0, 0, 10, 0).At(0, 0);
        _sessionText.At(0, 1);
        head.Children.Add(_liveDot);
        head.Children.Add(_sessionText);
        _cancelLaunch.At(0, 2);
        head.Children.Add(_cancelLaunch);
        _session = new Border
        {
            Background = Theme.WithAlpha(Color.FromRgb(8, 8, 10), 168), BorderBrush = Theme.White12, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 16, 18, 14),
            Child = Ui.VStack(head, stepGrid.M(0, 14, 0, 0), _attempt),
        };

        foreach (FrameworkElement part in new FrameworkElement[] { _idle, _transfer, _session })
        {
            part.Visibility = Visibility.Collapsed;
            part.VerticalAlignment = VerticalAlignment.Bottom;
            Children.Add(part);
        }
        Anim.Pulse(_liveDot, 0.35, 1, 1300);
        Refresh(animate: false);
    }

    private void OnCta()
    {
        switch (_app.ClientState)
        {
            case ClientState.Ready: _ = _app.PlayAsync(); break;
            case ClientState.NotInstalled or ClientState.UpdateAvailable: _ = _app.InstallOrUpdateAsync(); break;
            case ClientState.Unknown: _ = _app.LoadClientStateAsync(); break;
        }
    }

    public void Refresh(bool animate = true)
    {
        FrameworkElement target;
        var op = _app.Op;
        if (op is Operation.Installing or Operation.Verifying or Operation.Repairing or Operation.Uninstalling)
        {
            target = _transfer;
            _transfer.Update(_app.Transfer, op);
        }
        else if (op is Operation.Launching or Operation.Playing)
        {
            target = _session;
            UpdateSession();
        }
        else
        {
            target = _idle;
            UpdateIdle();
        }

        if (target == _shown) return;
        var previous = _shown;
        _shown = target;
        if (target == _transfer && previous != _transfer) _transfer.Reset();

        if (previous is not null) previous.Visibility = Visibility.Collapsed;
        target.Visibility = Visibility.Visible;
        if (animate) Anim.Rise(target, 0, 12, 380);
    }

    private void UpdateIdle()
    {
        var state = _app.ClientState;
        _cta.IsBusy = state == ClientState.Checking;
        _cta.IsEnabled = true;
        switch (state)
        {
            case ClientState.Ready:
                _cta.Text = "Jouer";
                _cta.SetIcon(Icons.Play);
                break;
            case ClientState.NotInstalled:
                _cta.Text = "Installer le jeu";
                _cta.SetIcon(Icons.Download);
                break;
            case ClientState.UpdateAvailable:
                _cta.Text = "Mettre à jour";
                _cta.SetIcon(Icons.Download);
                break;
            case ClientState.Checking:
                _cta.Text = "Vérification…";
                break;
            default:
                _cta.Text = "Réessayer";
                _cta.SetIcon(Icons.Refresh);
                break;
        }

        string text;
        Brush color;
        switch (state)
        {
            case ClientState.NotInstalled:
                text = $"{Fmt.Size(_app.ClientDownloadSize)} · reprise automatique si la connexion saute";
                color = Theme.InkDim;
                _dot.Background = Theme.InkMute;
                break;
            case ClientState.UpdateAvailable:
                text = $"{_app.InstalledVersion ?? "?"} → {_app.LatestVersion}";
                color = Theme.InkDim;
                _dot.Background = Theme.Gold;
                break;
            case ClientState.Unknown:
                text = "Versions indisponibles — vérifiez votre connexion";
                color = Theme.InkDim;
                _dot.Background = Theme.Err;
                break;
            default:
                if (_app.ServerOnline == true)
                {
                    text = $"Serveur de jeu en ligne · {_app.GameStatus?.Players ?? 0} joueurs";
                    color = Theme.InkDim;
                    _dot.Background = Theme.Ok;
                }
                else if (_app.ServerOnline == false)
                {
                    text = _app.GameStatus is {Maintenance:true} ? "Maintenance en cours" : _app.PingMs is null ? "Connexion impossible" : "Serveur de jeu hors ligne";
                    color = Theme.Err;
                    _dot.Background = Theme.Err;
                }
                else
                {
                    text = _app.PingMs is null ? "Vérification du serveur…" : "État du jeu inconnu";
                    color = Theme.InkMute;
                    _dot.Background = Theme.InkMute;
                }
                break;
        }
        _caption.Text = text;
        _caption.Foreground = color;
    }

    private void UpdateSession()
    {
        var play = _app.Play;
        var playing = _app.Op == Operation.Playing;
        _sessionText.Text = playing ? "En jeu — bon combat !" : play?.Text ?? "Démarrage…";
        _liveDot.Background = playing ? Theme.Ok : Theme.Ember;
        _cancelLaunch.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;

        var current = play?.Phase switch
        {
            PlayPhase.CheckingServer => 0,
            PlayPhase.PreparingTicket => 1,
            PlayPhase.Starting or PlayPhase.TitleScreen => 2,
            PlayPhase.Loading => 3,
            PlayPhase.InGame => 4,
            _ => 0,
        };
        if (playing) current = 4;
        for (var i = 0; i < _steps.Length; i++)
        {
            var done = i <= current;
            _steps[i].Background = done ? (i == 4 ? Theme.Ok : Theme.EmberGradientH) : Theme.White12;
            _steps[i].Opacity = i == current && !playing ? 0.75 : 1;
        }

        if (play is { Attempt: > 1 })
        {
            _attempt.Visibility = Visibility.Visible;
            _attemptText.Text = $"Tentative {play.Attempt}/{play.MaxAttempts}";
        }
        else
        {
            _attempt.Visibility = Visibility.Collapsed;
        }
    }
}
