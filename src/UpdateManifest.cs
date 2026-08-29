using System.Text.Json.Serialization;

namespace COTK.Launcher;

internal sealed record UpdateManifest(
    int SchemaVersion,
    LauncherRelease Launcher,
    ClientRelease Client);

internal sealed record LauncherRelease(
    string Version,
    string DownloadUrl,
    string Sha256,
    long SizeBytes,
    bool Required = false,
    // URL optionnelle de l'assistant d'installation (Inno Setup) publie pour
    // les nouveaux joueurs. Ignoree par l'auto-update, qui utilise le zip.
    string? SetupUrl = null);

internal sealed record ClientRelease(
    string Version,
    string ArchiveUrl,
    string Sha256,
    long SizeBytes,
    string Executable = "H1Z1.exe",
    bool Required = true,
    // Catalogue differentiel optionnel (JSON fichier par fichier). Present,
    // le launcher ne telecharge que ce qui a change au lieu des 15 Go.
    string? FilesUrl = null);

internal sealed record ClientFileEntry(string Path, long Size, string Sha256);

internal sealed record ClientFileCatalog(
    int SchemaVersion,
    string Version,
    int FileCount,
    long TotalBytes,
    string? BaseUrl,
    List<ClientFileEntry> Files);

internal sealed record LauncherUpdatePlan(
    string ArchivePath,
    string TargetDirectory,
    string LauncherExecutable,
    int ParentProcessId,
    string UpdaterExecutable);

internal sealed class UpdateException : Exception
{
    internal UpdateException(string message) : base(message) { }
    internal UpdateException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>L'utilisateur a refuse le telechargement du client : ce n'est
/// pas une erreur, on annule proprement le demarrage.</summary>
internal sealed class ClientDownloadDeclinedException : Exception
{
    internal ClientDownloadDeclinedException() : base("Telechargement du client refuse par l'utilisateur.") { }
}
