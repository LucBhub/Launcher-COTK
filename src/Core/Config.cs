using System.Reflection;

namespace COTK.Next.Core;

/// <summary>Configuration centrale. Les défauts sont la production. Les variables d'environnement
/// (dev local : JOUER.bat) ne sont acceptées QUE vers une adresse locale : un launcher de production
/// ne peut pas être redirigé vers un serveur distant (manifeste, mise à jour…) par l'environnement.</summary>
internal static class Config
{
    public const string ProdApi = "https://api.cotk.fr";
    public const string ProdGameServer = "164.132.200.95:20042";
    public const string SiteUrl = "https://cotk.fr";
    public const string DiscordUrl = "https://discord.gg/cotk";

    public static string ApiUrl => Local("COTK_API_URL", ProdApi, requireUrl: true);

    public static string GameServer => Local("COTK_GAME_SERVER", ProdGameServer, requireUrl: false);

    public static bool IsLocalApi => HostIsLoopback(new Uri(ApiUrl).Host);

    public static bool IsLocalGameServer
    {
        get
        {
            var value = GameServer;
            var i = value.LastIndexOf(':');
            return HostIsLoopback(i > 0 ? value[..i] : value);
        }
    }

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    public static bool HostIsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host == "127.0.0.1" || host == "::1" || host == "[::1]";

    /// <summary>Hôtes autorisés pour tout téléchargement : *.cotk.fr, ou une boucle locale en dev.</summary>
    public static bool IsAllowedDownloadHost(string host) =>
        host.Equals("dl.cotk.fr", StringComparison.OrdinalIgnoreCase)
        || host.Equals("api.cotk.fr", StringComparison.OrdinalIgnoreCase)
        || (IsLocalApi && HostIsLoopback(host));

    private static string Local(string name, string fallback, bool requireUrl)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        if (string.IsNullOrEmpty(value)) return fallback;

        string host;
        if (requireUrl)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return fallback;
            host = uri.Host;
        }
        else
        {
            var i = value.LastIndexOf(':');
            host = i > 0 ? value[..i] : value;
        }

        if (HostIsLoopback(host)) return value.TrimEnd('/');
        Log.Warn($"{name} ignoré (adresse non locale) — valeur de production utilisée");
        return fallback;
    }
}
