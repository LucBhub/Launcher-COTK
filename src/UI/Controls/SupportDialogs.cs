using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using COTK.Next.Core;

namespace COTK.Next.UI;

internal static class SupportDialogs
{
    private static TextBox Viewer() => new()
    {
        IsReadOnly = true, Height = 245, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Theme.Bg, Foreground = Theme.InkDim, BorderBrush = Theme.Line, BorderThickness = new Thickness(1),
        Padding = new Thickness(12), FontFamily = Theme.Mono, FontSize = 11.5,
    };

    public static void Submit(ModalHost modal, LauncherApp app, string? errorCode)
    {
        var card = new Card(18) { Width = 740, Padding = new Thickness(26), Background = Theme.CardHi };
        var body = new StackPanel();
        body.Children.Add(Ui.Display("Envoyer un extrait au support", 24));
        body.Children.Add(Ui.Text("Seul le texte affiché ci-dessous sera envoyé. Les secrets, e-mails et chemins utilisateur sont masqués. Conservation : 7 jours.", 13, Theme.InkDim, wrap:true).M(0,8,0,14));
        var preview = Viewer();
        var lineCount = 200;
        var client = new CheckBox { Content = Ui.Text("Inclure les journaux récents du jeu",13,Theme.InkDim), Foreground = Theme.InkDim, FontSize = 13, VerticalAlignment=VerticalAlignment.Center };
        var consent = new CheckBox { Content = Ui.Text("J’accepte d’envoyer cet extrait à l’équipe COTK (suppression après 7 jours).",12.5,Theme.Ink), Foreground=Theme.Ink, Margin=new Thickness(0,14,0,8) };
        var status = Ui.Text("", 12.5, Theme.InkDim, wrap:true);
        var send = new FxButton("Envoyer l’extrait", ButtonKind.Primary, Icons.External, 42, 14) { IsEnabled=false };
        var close = new FxButton("Fermer", ButtonKind.Ghost, null, 42);
        var copy = new FxButton("Copier", ButtonKind.Secondary, Icons.Copy, 42);
        var cts = new CancellationTokenSource();
        ReportSubmission? report = null;
        var generation = 0;
        var selectors = Ui.HStack();
        async Task Refresh()
        {
            var mine = ++generation;
            report = null; send.IsEnabled=false;
            preview.Text = L.T("Préparation de l’extrait…");
            try
            {
                var count = lineCount; var include = client.IsChecked==true;
                var result = await Task.Run(() => SupportReport.Prepare(count, include, app.InstalledVersion ?? "", errorCode));
                if (cts.IsCancellationRequested || mine != generation) return;
                report=result; preview.Text=SupportReport.Preview(result);
                status.Text = result.Sections.Count==0 ? "Aucun journal disponible." : $"{result.Sections.Count} source(s) · {preview.Text.Length:N0} caractères · extrait limité en taille";
                if (app.Session is null && !app.Preview) status.Text += " · Connectez-vous pour envoyer.";
                send.IsEnabled = consent.IsChecked==true && result.Sections.Count>0 && (app.Session is not null || app.Preview);
            }
            catch(Exception ex) { if (!cts.IsCancellationRequested) { preview.Text=""; status.Text=Errors.From(ex).Report(); } }
        }
        foreach (var count in new[] {50,200,500})
        {
            var choose = new FxButton($"{count} lignes max.", ButtonKind.Secondary, null, 34, 12) { Margin=new Thickness(0,0,8,0) };
            choose.Click += async (_,_) => { lineCount=count; await Refresh(); };
            selectors.Children.Add(choose);
        }
        body.Children.Add(selectors);
        body.Children.Add(client.M(0,12,0,12));
        body.Children.Add(preview);
        body.Children.Add(consent);
        body.Children.Add(status);
        client.Checked += async (_,_) => await Refresh(); client.Unchecked += async (_,_) => await Refresh();
        consent.Checked += (_,_) => send.IsEnabled=report?.Sections.Count>0 && (app.Session is not null || app.Preview);
        consent.Unchecked += (_,_) => send.IsEnabled=false;
        close.Click += (_,_) => modal.Hide();
        copy.Click += (_,_) => { if(preview.Text.Length>0) { Clipboard.SetText(preview.Text); copy.Text="Copié ✓"; } };
        send.Click += async (_,_) =>
        {
            if (report is null || consent.IsChecked!=true) return;
            send.IsEnabled=false; send.IsBusy=true;
            client.IsEnabled=false; selectors.IsEnabled=false; consent.IsEnabled=false;
            try
            {
                var receipt = await app.SendReportAsync(report with { Consent=true },cts.Token);
                status.Foreground=Theme.Ok;
                status.Text=$"Rapport reçu : {receipt.Id}\nSuppression : {receipt.ExpiresAt.ToLocalTime():dd/MM/yyyy HH:mm}. Communiquez ce numéro au support.";
                copy.Text="Copier le numéro";
                copy.Click += (_,_) => Clipboard.SetText(receipt.Id);
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) { status.Foreground=Theme.Err; status.Text=Errors.From(ex).Report(); send.IsEnabled=true; }
            finally { send.IsBusy=false; }
        };
        body.Children.Add(Ui.HStack(close,copy.M(8,0,0,0),send.M(8,0,0,0)).Align(HorizontalAlignment.Right).M(0,16,0,0));
        card.Child=body;
        modal.Show(card, closed:() => cts.Cancel());
        _ = Refresh();
    }

    public static async Task ReadReceived(ModalHost modal, LauncherApp app)
    {
        var card = new Card(18) { Width=740, Padding=new Thickness(26), Background=Theme.CardHi };
        var body=Ui.VStack(Ui.Display("Rapports reçus · support",24),Ui.Text("Accès réservé au staff. Les rapports expirent après sept jours.",13,Theme.InkDim).M(0,8,0,14));
        var viewer=Viewer(); viewer.Height=340;
        var list=new StackPanel();
        var scroll=new ScrollViewer { Content=list, MaxHeight=180, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        body.Children.Add(scroll); body.Children.Add(viewer.M(0,12,0,0));
        var close=new FxButton("Fermer", ButtonKind.Secondary, null,38).Align(HorizontalAlignment.Right).M(0,14,0,0);
        close.Click+=(_,_)=>modal.Hide(); body.Children.Add(close); card.Child=body;
        var cts=new CancellationTokenSource(); modal.Show(card,closed:()=>cts.Cancel());
        try
        {
            if (app.Preview) { viewer.Text=L.T("Mode aperçu : aucun rapport réel n’est chargé.");return; }
            var token=app.Session?.AccessToken ?? throw new LauncherException(Errors.SessionExpired());
            var reports=await app.Api.ListReportsAsync(token,cts.Token);
            if(reports.Reports.Count==0) viewer.Text=L.T("Aucun rapport actif.");
            foreach(var report in reports.Reports)
            {
                var button=new FxButton($"{report.Username} · {report.CreatedAt.ToLocalTime():dd/MM HH:mm} · {report.ErrorCode} · {report.Id}",ButtonKind.Ghost,null,34,12);
                button.Click+=async(_,_)=>
                {
                    try { viewer.Text=L.T("Chargement…"); var details=await app.Api.ReadReportAsync(token,report.Id,CancellationToken.None); viewer.Text=L.T($"Rapport {details.Id} · expire le {details.ExpiresAt.ToLocalTime():dd/MM HH:mm}")+"\n\n"+details.Excerpt; }
                    catch(Exception ex) { viewer.Text=L.T(Errors.From(ex).Report()); }
                };
                list.Children.Add(button);
            }
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { viewer.Text=L.T(Errors.From(ex).Report()); }
    }
}
