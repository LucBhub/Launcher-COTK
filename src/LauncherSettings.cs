using System.Text.Json;

namespace COTK.Launcher;

/// <summary>Reglages persistants du launcher (%APPDATA%\COTK\settings.json).
/// Survivent aux mises a jour du launcher, contrairement au dossier d'install.</summary>
internal static class LauncherSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "COTK");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static string? ClientDirectory { get; private set; }

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
