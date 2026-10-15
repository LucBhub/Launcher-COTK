namespace COTK.Next.Core;

/// <summary>Tous les emplacements du launcher au meme endroit. Reglages dans
/// %APPDATA%\COTK (partages avec l'ancien launcher : le dossier du client est
/// retrouve tel quel), logs dans %LOCALAPPDATA%\COTK\logs.</summary>
internal static class AppPaths
{
    // COTK_SETTINGS_DIR / COTK_LOG_DIR : isolation des tests.
    public static string SettingsDir =>
        Environment.GetEnvironmentVariable("COTK_SETTINGS_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "COTK");

    public static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

    public static string LogDir =>
        Environment.GetEnvironmentVariable("COTK_LOG_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "COTK", "logs");

    public static string DiagnosticsDir => Path.Combine(LogDir, "diagnostics");

    /// <summary>Dossier d'installation du launcher (a cote de l'executable).</summary>
    public static string InstallDir => AppContext.BaseDirectory;

    public static string FontsDir => Path.Combine(InstallDir, "fonts");
}
