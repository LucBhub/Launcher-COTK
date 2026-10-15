namespace COTK.Next.Core;

internal sealed record UpdateManifest(int SchemaVersion, LauncherRelease Launcher, ClientRelease Client);

internal sealed record LauncherRelease(
    string Version,
    string DownloadUrl,
    string Sha256,
    long SizeBytes,
    bool Required = false,
    string? SetupUrl = null);

internal sealed record ClientRelease(
    string Version,
    string ArchiveUrl,
    string Sha256,
    long SizeBytes,
    string Executable = "H1Z1.exe",
    bool Required = true,
    string? FilesUrl = null,
    string? FilesSha256 = null,
    long ExpandedSizeBytes = 0);

internal sealed record ClientFileEntry(string Path, long Size, string Sha256);

internal sealed record ClientFileCatalog(
    int SchemaVersion,
    string Version,
    int FileCount,
    long TotalBytes,
    string? BaseUrl,
    List<ClientFileEntry> Files);

/// <summary>Ce que l'installateur a réellement fait : l'UI peut dire « 3 fichiers mis à jour » au lieu d'un « prêt » muet.</summary>
internal sealed record ClientSyncResult(bool WasUpdate, bool FullArchive, int FilesChecked, int FilesDownloaded, long BytesDownloaded)
{
    public static readonly ClientSyncResult AlreadyCurrent = new(false, false, 0, 0, 0);
}

internal enum TransferPhase { Preparing, Analyzing, Downloading, Verifying, Extracting, Installing, Cleaning, Done, Uninstalling, Paused }

/// <summary>Progression structurée : l'UI affiche barre, vitesse, temps restant sans parser de texte.</summary>
internal sealed record TransferProgress(
    TransferPhase Phase,
    string Message,
    long BytesDone = 0,
    long BytesTotal = 0,
    double BytesPerSecond = 0,
    TimeSpan? Eta = null,
    int FilesDone = 0,
    int FilesTotal = 0)
{
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Clamp((double)BytesDone / BytesTotal, 0, 1);

    public bool Determinate => BytesTotal > 0;
}

/// <summary>Vitesse lissée (fenêtre glissante ~5 s) et temps restant.</summary>
internal sealed class Throughput
{
    private readonly Queue<(long ticks, long bytes)> _samples = new();
    private readonly TimeSpan _window;
    private readonly Func<long> _clock;

    public Throughput(TimeSpan? window = null, Func<long>? clock = null)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
        _clock = clock ?? (() => System.Diagnostics.Stopwatch.GetTimestamp());
    }

    public double BytesPerSecond { get; private set; }

    public void Add(long totalBytes)
    {
        var now = _clock();
        _samples.Enqueue((now, totalBytes));
        var limit = now - (long)(_window.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
        while (_samples.Count > 2 && _samples.Peek().ticks < limit) _samples.Dequeue();
        if (_samples.Count < 2) return;
        var first = _samples.Peek();
        var seconds = (now - first.ticks) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (seconds > 0.2) BytesPerSecond = Math.Max(0, (totalBytes - first.bytes) / seconds);
    }

    public TimeSpan? Eta(long remaining) =>
        BytesPerSecond > 1 && remaining >= 0 ? TimeSpan.FromSeconds(remaining / BytesPerSecond) : null;
}

internal static class ManifestRules
{
    public static void Validate(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new LauncherException(Errors.UnsafeUpdate($"Version de manifeste non prise en charge : {manifest.SchemaVersion}."));
        if (manifest.Client is null || manifest.Launcher is null)
            throw new LauncherException(Errors.Manifest("manifeste incomplet"));

        ValidateRelease("launcher", manifest.Launcher.Version, manifest.Launcher.DownloadUrl, manifest.Launcher.Sha256, manifest.Launcher.SizeBytes, required: false);
        ValidateRelease("client", manifest.Client.Version, manifest.Client.ArchiveUrl, manifest.Client.Sha256, manifest.Client.SizeBytes, required: true);

        if (!string.IsNullOrWhiteSpace(manifest.Client.FilesUrl)) ValidateUrl(manifest.Client.FilesUrl);
        if (string.IsNullOrWhiteSpace(manifest.Client.Executable) || !IsSafeRelative(manifest.Client.Executable))
            throw new LauncherException(Errors.UnsafeUpdate("Le chemin de l'exécutable du client est invalide."));
    }

    private static void ValidateRelease(string name, string version, string url, string sha256, long size, bool required)
    {
        if (!Version.TryParse(version, out _))
            throw new LauncherException(Errors.UnsafeUpdate($"Version {name} invalide : {version}."));
        if (size < 0 || sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new LauncherException(Errors.UnsafeUpdate($"Empreinte ou taille invalide pour la release {name} {version}."));
        if (required && string.IsNullOrWhiteSpace(url))
            throw new LauncherException(Errors.UnsafeUpdate($"URL absente pour la release {name} {version}."));
        if (!string.IsNullOrEmpty(url)) ValidateUrl(url);
    }

    /// <summary>HTTPS obligatoire, pas de login dans l'URL, et seulement nos propres domaines
    /// (*.cotk.fr). HTTP uniquement vers la boucle locale en dev.</summary>
    public static void ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0)
            throw new LauncherException(Errors.UnsafeUpdate("Adresse de téléchargement invalide."));

        var secure = uri.Scheme == Uri.UriSchemeHttps;
        var devLoopback = uri.Scheme == Uri.UriSchemeHttp && Config.HostIsLoopback(uri.Host) && Config.IsLocalApi;
        if (!secure && !devLoopback)
            throw new LauncherException(Errors.UnsafeUpdate("Un téléchargement doit utiliser HTTPS."));
        if (!Config.IsAllowedDownloadHost(uri.Host))
            throw new LauncherException(Errors.UnsafeUpdate($"Hôte de téléchargement non autorisé : {uri.Host}."));
    }

    /// <summary>Chemin relatif sûr : ni racine, ni lecteur, ni remontée, ni caractère interdit.</summary>
    public static bool IsSafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\')) return false;
        if (path.Contains(':') || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part is "" or "." or "..") return false;
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            var name = part.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" ||
                name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && name[3] is >= '1' and <= '9') return false;
            if (part.EndsWith(' ') || part.EndsWith('.')) return false;
        }
        return true;
    }

    public static bool IsNewer(string remote, string local) =>
        Version.TryParse(remote, out var r) && Version.TryParse(local, out var l) && r > l;

    /// <summary>Données du joueur : jamais écrasées, jamais supprimées, jamais hachées.</summary>
    public static bool IsPlayerData(string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/');
        var rootSettings = !relativePath.Contains('/')
            && (relativePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                || relativePath.Equals("SoundSettings.xml", StringComparison.OrdinalIgnoreCase)
                || (relativePath.StartsWith("InputProfile_", StringComparison.OrdinalIgnoreCase)
                    && relativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
        return rootSettings
            || relativePath.Equals(".cotk-client-version", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(".cotk-hash-cache.json", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(".cotk-managed-files.json", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".cotk-part", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("Logs", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("Logs/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("UI/Settings", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("UI/Settings/", StringComparison.OrdinalIgnoreCase);
    }
}
