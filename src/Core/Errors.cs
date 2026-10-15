using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace COTK.Next.Core;

/// <summary>Une erreur affichable : un code stable (pour le support), un titre humain,
/// ce qui s'est passé, quoi faire, et le détail technique (copiable, jamais affiché brut).</summary>
internal sealed record LauncherError(
    string Code,
    string Title,
    string Message,
    string? Hint = null,
    bool Retryable = false,
    string? Technical = null)
{
    public string Report() =>
        $"[{Code}] {Title}{Environment.NewLine}{Message}"
        + (Hint is null ? "" : $"{Environment.NewLine}→ {Hint}")
        + (Technical is null ? "" : $"{Environment.NewLine}Détail : {Technical}");
}

internal sealed class LauncherException : Exception
{
    public LauncherError Error { get; }

    public LauncherException(LauncherError error, Exception? inner = null)
        : base(error.Message, inner) => Error = error;
}

/// <summary>L'utilisateur a décliné (dossier, téléchargement…) : ce n'est pas une erreur.</summary>
internal sealed class UserDeclinedException : Exception
{
    public UserDeclinedException(string message = "Action annulée.") : base(message) { }
}

/// <summary>Catalogue des erreurs. Les codes ne changent JAMAIS : ils figurent dans les captures
/// d'écran des joueurs et dans le support.</summary>
internal static class Errors
{
    // ------------------------------------------------------------- réseau
    public static LauncherError Offline(string? tech = null) => new("E-NET-001",
        "Pas de connexion",
        "Impossible de joindre les serveurs COTK.",
        "Vérifiez votre connexion Internet (ou un pare-feu / VPN), puis réessayez.", true, tech);

    public static LauncherError Timeout(string? tech = null) => new("E-NET-002",
        "Le serveur met trop de temps à répondre",
        "La requête a expiré.",
        "Votre connexion est peut-être instable. Réessayez dans un instant.", true, tech);

    public static LauncherError Tls(string? tech = null) => new("E-NET-003",
        "Connexion sécurisée impossible",
        "Le certificat du serveur n'a pas pu être vérifié.",
        "Vérifiez la date et l'heure de votre PC (un décalage invalide les certificats), puis réessayez.", true, tech);

    public static LauncherError RateLimited(TimeSpan? retryAfter) => new("E-NET-004",
        "Trop de tentatives",
        retryAfter is { } wait
            ? $"Réessayez dans {Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))} secondes."
            : "Patientez une minute avant de réessayer.",
        null, true);

    public static LauncherError Server(int status, string? tech = null) => new("E-NET-005",
        "Les serveurs COTK rencontrent un problème",
        $"Le service a répondu par une erreur (HTTP {status}).",
        "Ce n'est pas de votre côté. Réessayez dans quelques minutes.", true, tech);

    public static LauncherError BadResponse(string? tech = null) => new("E-NET-006",
        "Réponse inattendue du serveur",
        "Le serveur a renvoyé une réponse que le launcher ne comprend pas.",
        "Mettez à jour le launcher ou réessayez plus tard.", true, tech);

    // ------------------------------------------------------------- compte
    public static LauncherError BadCredentials() => new("E-AUTH-001",
        "Identifiants incorrects",
        "Le pseudo (ou l'e-mail) ou le mot de passe est faux.",
        "Mot de passe oublié ? Réinitialisez-le sur cotk.fr.");

    public static LauncherError Banned() => new("E-AUTH-002",
        "Compte suspendu",
        "Ce compte n'a pas le droit de jouer actuellement.",
        "Contactez l'équipe COTK sur Discord avec ce code.");

    public static LauncherError SessionExpired() => new("E-AUTH-003",
        "Session expirée",
        "Votre session a expiré ou a été révoquée.",
        "Reconnectez-vous.");

    public static LauncherError DeviceExpired() => new("E-AUTH-004",
        "Code expiré",
        "Le code de connexion n'est plus valide.",
        "Relancez la connexion pour obtenir un nouveau code.", true);

    public static LauncherError NotVerified() => new("E-AUTH-005",
        "Compte non vérifié",
        "Votre adresse e-mail n'est pas encore confirmée.",
        "Cliquez sur le lien reçu par e-mail, puis reconnectez-vous.");

    // ------------------------------------------------------------- disque
    public static LauncherError DiskFull(long needed, long free) => new("E-DISK-001",
        "Espace disque insuffisant",
        needed > 0
            ? $"Il faut {Fmt.Size(needed)} de libre, il n'en reste que {Fmt.Size(free)}."
            : "Le disque est plein.",
        "Libérez de l'espace ou choisissez un autre disque dans Paramètres.", false);

    public static LauncherError DiskAccess(string path, string? tech = null) => new("E-DISK-002",
        "Accès refusé à un fichier",
        $"Windows ou un antivirus bloque l'accès à : {path}",
        "Fermez le jeu, ajoutez le dossier du jeu aux exclusions de votre antivirus, puis réessayez.", true, tech);

    public static LauncherError PathTooLong(string path) => new("E-DISK-003",
        "Chemin trop long",
        $"Le chemin est trop long pour Windows : {path}",
        "Choisissez un dossier plus court (par exemple D:\\Games\\COTK).");

    public static LauncherError UnsafeFolder(string path) => new("E-DISK-004",
        "Dossier non adapté",
        $"Le dossier choisi existe déjà et n'est pas vide : {path}",
        "Choisissez un dossier dédié ou videz-le.");

    // ------------------------------------------------------------- mises à jour
    public static LauncherError Manifest(string? tech = null) => new("E-UPD-001",
        "Informations de version indisponibles",
        "Le launcher n'a pas pu lire la liste des versions publiées.",
        "Réessayez dans un instant.", true, tech);

    public static LauncherError HashMismatch(string what) => new("E-UPD-002",
        "Fichier corrompu",
        $"La vérification d'intégrité a échoué pour : {what}.",
        "Le téléchargement sera refait. Si cela persiste, lancez Vérifier les fichiers.", true);

    public static LauncherError RemoteFileMissing(string what) => new("E-UPD-003",
        "Fichier introuvable sur le serveur",
        $"Un fichier annoncé par le catalogue est absent du serveur de téléchargement : {what}.",
        "Une mise à jour est probablement en cours de publication. Réessayez dans quelques minutes.", true);

    public static LauncherError UnsafeUpdate(string why) => new("E-UPD-004",
        "Mise à jour refusée par sécurité",
        why,
        "Cette mise à jour ne sera pas appliquée. Prévenez l'équipe COTK avec ce code.");

    // ------------------------------------------------------------- jeu
    public static LauncherError NotInstalled() => new("E-GAME-001",
        "Jeu non installé",
        "Le client du jeu n'est pas installé (ou pas à jour).",
        "Ouvrez Bibliothèque pour l'installer.");

    public static LauncherError AlreadyRunning() => new("E-GAME-002",
        "Le jeu est déjà ouvert",
        "Une instance de H1Z1 tourne déjà.",
        "Fermez-la avant de relancer.");

    public static LauncherError ServerDown() => new("E-GAME-003",
        "Serveur de jeu hors ligne",
        "Les serveurs de COTK ne répondent pas pour le moment.",
        "Réessayez dans quelques minutes (l'état est affiché en haut à droite).", true);

    public static LauncherError ClientCrashedAtStart(int attempts, string? exit) => new("E-GAME-004",
        "Le jeu se ferme au démarrage",
        $"Le client a planté {attempts} fois de suite{(exit is null ? "" : $" (code {exit})")}.",
        "1) Bibliothèque › Vérifier les fichiers · 2) Ajoutez le dossier du jeu aux exclusions de votre antivirus · "
        + "3) Mettez à jour vos pilotes graphiques. Joignez le rapport si le problème persiste.", true, exit);

    public static LauncherError TicketRejected() => new("E-GAME-005",
        "Session de jeu refusée",
        "Le serveur de jeu a refusé votre session de connexion.",
        "Reconnectez-vous au launcher puis relancez. Si cela persiste, exportez le rapport.", true);

    public static LauncherError LoadTimeout() => new("E-GAME-006",
        "Le chargement du monde ne se termine pas",
        "Le jeu est resté bloqué sur l'écran de chargement.",
        "Réessayez. Si le serveur est chargé, patientez quelques minutes.", true);

    public static LauncherError ExeMissing(string path) => new("E-GAME-007",
        "H1Z1.exe introuvable",
        $"Le fichier est absent : {path}",
        "Lancez Vérifier les fichiers dans Bibliothèque.");

    public static LauncherError LaunchFailed(string? tech) => new("E-GAME-008",
        "Impossible de démarrer le jeu",
        "Windows a refusé de lancer le client.",
        "Vérifiez que l'antivirus n'a pas mis H1Z1.exe en quarantaine.", true, tech);

    // ------------------------------------------------------------- application
    public static LauncherError Unexpected(Exception ex) => new("E-APP-001",
        "Erreur inattendue",
        "Quelque chose s'est mal passé dans le launcher.",
        "Réessayez. Si cela se reproduit, exportez le rapport et envoyez-le au support.", true,
        $"{ex.GetType().Name}: {ex.Message}");

    public static LauncherError InvalidConfig(string why) => new("E-APP-002",
        "Configuration invalide",
        why,
        "Réinstallez le launcher depuis cotk.fr.");

    /// <summary>Convertit n'importe quelle exception en erreur affichable.</summary>
    public static LauncherError From(Exception ex)
    {
        switch (ex)
        {
            case LauncherException le:
                return le.Error;
            case AggregateException ag when ag.InnerExceptions.Count > 0:
                return From(ag.InnerExceptions[0]);
            case TaskCanceledException or TimeoutException:
                return Timeout(ex.Message);
            case HttpRequestException http:
                return FromHttpRequest(http);
            case AuthenticationException tls:
                return Tls(tls.Message);
            case PathTooLongException ptl:
                return PathTooLong(ptl.Message);
            case UnauthorizedAccessException ua:
                return DiskAccess(ua.Message, ua.GetType().Name);
            case IOException io when IsDiskFull(io):
                return DiskFull(0, 0) with { Technical = io.Message };
            case IOException io:
                return DiskAccess(io.Message, io.GetType().Name);
            default:
                return Unexpected(ex);
        }
    }

    internal static LauncherError FromHttpRequest(HttpRequestException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is AuthenticationException) return Tls(e.Message);
            if (e is SocketException) return Offline(e.Message);
        }
        if (ex.StatusCode is { } status) return FromStatus((int)status, null, null);
        return Offline(ex.Message);
    }

    /// <summary>Statut HTTP + message d'API (<c>{"error":"..."}</c>) vers une erreur claire.</summary>
    public static LauncherError FromStatus(int status, string? apiMessage, TimeSpan? retryAfter)
    {
        var msg = (apiMessage ?? "").ToLowerInvariant();
        return status switch
        {
            401 when msg.Contains("expired") || msg.Contains("authentication required") => SessionExpired(),
            401 => BadCredentials(),
            403 when msg.Contains("ban") => Banned(),
            403 when msg.Contains("verif") => NotVerified(),
            403 => Banned(),
            429 => RateLimited(retryAfter),
            >= 500 => Server(status, apiMessage),
            400 when msg.Contains("expired") || msg.Contains("invalid") => DeviceExpired(),
            _ => BadResponse($"HTTP {status}: {apiMessage}"),
        };
    }

    private static bool IsDiskFull(IOException ex)
    {
        // ERROR_DISK_FULL (112) / ERROR_HANDLE_DISK_FULL (39).
        var code = ex.HResult & 0xFFFF;
        return code is 112 or 39;
    }
}

/// <summary>Formats d'affichage partagés.</summary>
internal static class Fmt
{
    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", Fr) + " Go",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", Fr) + " Mo",
        >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("0", Fr) + " Ko",
        _ => $"{bytes} o",
    };

    public static string Speed(double bytesPerSecond) => bytesPerSecond <= 0 ? "—" : Size((long)bytesPerSecond) + "/s";

    public static string Eta(TimeSpan? eta)
    {
        if (eta is not { } t || t < TimeSpan.Zero) return "—";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes:00} min";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min {t.Seconds:00} s";
        return $"{Math.Max(1, (int)t.TotalSeconds)} s";
    }

    public static string Number(long value) => value.ToString("N0", Fr).Replace('\u202f', ' ').Replace('\u00a0', ' ');

    public static string Relative(DateTimeOffset when, DateTimeOffset? now = null)
    {
        var span = (now ?? DateTimeOffset.UtcNow) - when;
        if (span.TotalMinutes < 2) return "À l'instant";
        if (span.TotalHours < 1) return $"Il y a {(int)span.TotalMinutes} min";
        if (span.TotalDays < 1) return $"Il y a {(int)span.TotalHours} h";
        if (span.TotalDays < 2) return "Hier";
        if (span.TotalDays < 30) return $"Il y a {(int)span.TotalDays} jours";
        return when.ToLocalTime().ToString("d MMM yyyy", Fr);
    }
}
