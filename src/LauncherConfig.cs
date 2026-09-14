namespace COTK.Launcher;

/// <summary>Configuration centrale du launcher. Defauts = production ; le
/// developpement local surcharge via variables d'environnement (JOUER.bat).</summary>
internal static class LauncherConfig
{
    public static string ApiUrl => Get("COTK_API_URL", "https://api.cotk.fr");
    public static string GameServer => Get("COTK_GAME_SERVER", "164.132.200.95:20042");

    /// <summary>Vrai si le serveur de jeu est suppose tourner sur cette machine
    /// (mode dev/LAN local). Pilote la verification des ports UDP.</summary>
    public static bool GameServerIsLocal
    {
        get
        {
            var host = GameServer[..GameServer.LastIndexOf(':')];
            return host is "127.0.0.1" or "::1" or "localhost";
        }
    }

    private static string Get(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
