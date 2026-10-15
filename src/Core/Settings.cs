using System.Text.Json;
using System.Text.Json.Nodes;

namespace COTK.Next.Core;

/// <summary>Réglages persistants (%APPDATA%\COTK\settings.json), partagés avec l'ancien launcher :
/// la clé <c>clientDir</c> est conservée à l'identique. Les clés inconnues sont préservées à
/// l'écriture, l'écriture est atomique, un fichier corrompu retombe sur les défauts.</summary>
internal static class Settings
{
    private static readonly object Gate = new();
    private static JsonObject _root = new();

    public static event Action? Changed;

    public static string Language
    {
        get => L.NormalizeLanguage(ReadString("language"));
        set => Write("language", L.NormalizeLanguage(value));
    }

    public static string? ClientDirectory
    {
        get => ReadString("clientDir") is { } dir && Path.IsPathRooted(dir) ? dir : null;
        set => Write("clientDir", value is null ? null : Path.GetFullPath(value));
    }

    /// <summary>Réduit les animations (accessibilité / PC modestes). Défaut : suit Windows.</summary>
    public static bool ReduceMotion
    {
        get => ReadBool("reduceMotion") ?? !System.Windows.SystemParameters.ClientAreaAnimation;
        set => Write("reduceMotion", value);
    }

    /// <summary>Réduit le launcher dans la barre des tâches pendant que le jeu tourne.</summary>
    public static bool MinimizeOnPlay
    {
        get => ReadBool("minimizeOnPlay") ?? true;
        set => Write("minimizeOnPlay", value);
    }

    public static bool VerboseLogs
    {
        get => ReadBool("verboseLogs") ?? false;
        set { Write("verboseLogs", value); Log.Minimum = value ? LogLevel.Debug : LogLevel.Info; }
    }

    public static void Load()
    {
        lock (Gate)
        {
            _root = new JsonObject();
            try
            {
                if (!File.Exists(AppPaths.SettingsFile)) return;
                // BOM toléré : un fichier réécrit par PowerShell ne doit pas faire échouer le chargement.
                var text = File.ReadAllText(AppPaths.SettingsFile).TrimStart('\uFEFF').Trim();
                if (text.Length == 0) return;
                if (JsonNode.Parse(text) is JsonObject obj) _root = obj;
            }
            catch (Exception ex)
            {
                Log.Warn("settings.json illisible, valeurs par défaut utilisées", ex);
                _root = new JsonObject();
            }
        }
        Log.Minimum = VerboseLogs ? LogLevel.Debug : LogLevel.Info;
    }

    private static string? ReadString(string key)
    {
        lock (Gate)
            return _root[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
    }

    private static bool? ReadBool(string key)
    {
        lock (Gate)
            return _root[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    }

    private static void Write(string key, JsonNode? value)
    {
        lock (Gate)
        {
            if (value is null) _root.Remove(key);
            else _root[key] = value;
            Save();
        }
        Changed?.Invoke();
    }

    private static void Write(string key, string? value) => Write(key, value is null ? null : JsonValue.Create(value));
    private static void Write(string key, bool value) => Write(key, JsonValue.Create(value));

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDir);
            var temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("settings.json non enregistré", ex);
        }
    }
}
