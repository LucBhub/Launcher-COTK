using System.Text.Json;

namespace COTK.Launcher;

/// <summary>Reglages persistants du launcher (%APPDATA%\COTK\settings.json).
/// Survivent aux mises a jour du launcher, contrairement au dossier d'install.</summary>
internal static class LauncherSettings
{
    // COTK_SETTINGS_DIR : isolation des tests (sinon ils ecraseraient les
    // vrais reglages de l'utilisateur dans %APPDATA%\COTK). Propriete et non
    // champ statique : la variable doit etre lue a chaque acces, quel que soit
    // l'ordre d'initialisation des classes de test.
    private static string SettingsDir =>
        Environment.GetEnvironmentVariable("COTK_SETTINGS_DIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "COTK");

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static string? ClientDirectory { get; internal set; }

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("clientDir", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                var dir = value.GetString();
                if (!string.IsNullOrWhiteSpace(dir) && Path.IsPathRooted(dir))
                    ClientDirectory = dir;
            }
        }
        catch
        {
            // Reglages corrompus : on repart sur les defauts.
            ClientDirectory = null;
        }
    }

    public static void SetClientDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            throw new ArgumentException("Chemin de client invalide.", nameof(path));
        ClientDirectory = Path.GetFullPath(path);
        Save();
    }

    private static void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var payload = JsonSerializer.Serialize(new { clientDir = ClientDirectory }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, payload);
    }
}
