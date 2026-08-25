using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace COTK.Launcher;

internal sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
    };

    private readonly HttpClient _http;
    private readonly string _apiUrl;

    internal UpdateService()
    {
_apiUrl = LauncherConfig.ApiUrl.TrimEnd('/');
        if (!Uri.TryCreate(_apiUrl + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || (baseUri.Scheme == Uri.UriSchemeHttp && !baseUri.IsLoopback))
        {
            throw new InvalidOperationException("COTK_API_URL must be an absolute HTTP or HTTPS URL.");
        }

        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(30) };
    }

    internal async Task<UpdateManifest?> GetManifestAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("api/v1/launcher/updates", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new UpdateException($"Le manifeste de versions a répondu HTTP {(int)response.StatusCode}.");

        try
        {
            var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Manifeste vide.");
            ValidateManifest(manifest);
            return manifest;
        }
        catch (JsonException ex)
        {
            throw new UpdateException("Le manifeste de versions est invalide.", ex);
        }
    }

    internal async Task<UpdateResult> EnsureAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var manifest = await GetManifestAsync(cancellationToken);
        if (manifest is null)
            return new UpdateResult(false, false, "Manifeste de versions indisponible.");

        if (IsNewer(manifest.Launcher.Version, CurrentLauncherVersion()))
        {
            if (string.IsNullOrWhiteSpace(manifest.Launcher.DownloadUrl))
            {
                if (manifest.Launcher.Required)
                    throw new UpdateException("Une mise à jour obligatoire du launcher est publiée sans archive.");
            }
            else
            {
                progress?.Report($"Mise à jour du launcher {manifest.Launcher.Version}...");
                await ScheduleLauncherUpdateAsync(manifest.Launcher, progress, cancellationToken);
                return new UpdateResult(true, true, "Le launcher va redémarrer pour appliquer la mise à jour.");
            }
        }

        await EnsureClientAsync(manifest.Client, progress, cancellationToken);
        return new UpdateResult(true, false, $"Versions validées : launcher {CurrentLauncherVersion()}, client {manifest.Client.Version}.", manifest.Client.Version);
    }

    internal static string CurrentLauncherVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    internal static bool IsNewer(string remote, string local)
    {
        if (!Version.TryParse(remote, out var remoteVersion) || !Version.TryParse(local, out var localVersion))
            throw new UpdateException($"Version invalide : remote={remote}, local={local}.");
        return remoteVersion > localVersion;
    }

    internal static void ValidateManifest(UpdateManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new UpdateException($"Version de manifeste non supportée : {manifest.SchemaVersion}.");
        ValidateRelease(manifest.Launcher.Version, manifest.Launcher.DownloadUrl, manifest.Launcher.Sha256, manifest.Launcher.SizeBytes, false);
        ValidateRelease(manifest.Client.Version, manifest.Client.ArchiveUrl, manifest.Client.Sha256, manifest.Client.SizeBytes, true);
        if (string.IsNullOrWhiteSpace(manifest.Client.Executable)
            || Path.IsPathRooted(manifest.Client.Executable)
            || manifest.Client.Executable.Contains("..", StringComparison.Ordinal))
            throw new UpdateException("Le chemin de l'exécutable client est invalide.");
    }

    private static void ValidateRelease(string version, string url, string sha256, long sizeBytes, bool required)
    {
        if (!Version.TryParse(version, out _))
            throw new UpdateException($"Version de release invalide : {version}.");
        if (sizeBytes < 0 || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new UpdateException($"Empreinte ou taille invalide pour la release {version}.");
        if (required && string.IsNullOrWhiteSpace(url))
            throw new UpdateException($"URL absente pour la release obligatoire {version}.");
        if (url.Length > 0)
            ValidateDownloadUrl(url);
    }

    private static void ValidateDownloadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || (uri.Scheme == Uri.UriSchemeHttp
                && !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase)))
            throw new UpdateException("Une archive distante doit utiliser HTTPS; HTTP est réservé à localhost.");
    }

    private async Task EnsureClientAsync(ClientRelease release, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var clientDir = GameLauncher.ClientDir;
        var executable = Path.Combine(clientDir, release.Executable);
        var marker = Path.Combine(clientDir, ".cotk-client-version");
        if (File.Exists(executable)
            && File.Exists(marker)
            && string.Equals(File.ReadAllText(marker).Trim(), release.Version, StringComparison.Ordinal))
            return;

        if (string.IsNullOrWhiteSpace(release.ArchiveUrl))
        {
            if (File.Exists(executable))
            {
                File.WriteAllText(marker, release.Version + Environment.NewLine);
                GameLauncher.Log($"Existing client accepted as version {release.Version}; no archive configured");
                return;
            }
            throw new UpdateException("Le client est absent et aucune archive n'est configurée.");
        }

        progress?.Report($"Téléchargement du client {release.Version}...");
        var archive = await DownloadVerifiedAsync(release.ArchiveUrl, release.Sha256, release.SizeBytes, "client", progress, cancellationToken);
        await InstallClientArchiveAsync(archive, release, progress, cancellationToken);
    }

    private async Task ScheduleLauncherUpdateAsync(
        LauncherRelease release,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var archive = await DownloadVerifiedAsync(release.DownloadUrl, release.Sha256, release.SizeBytes, "launcher", progress, cancellationToken);
        var planPath = Path.Combine(Path.GetTempPath(), $"cotk-launcher-update-{Guid.NewGuid():N}.json");
        var updaterDirectory = Path.Combine(Path.GetTempPath(), $"COTK.Launcher.Updater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(updaterDirectory);
        foreach (var runtimeFile in Directory.EnumerateFiles(AppContext.BaseDirectory, "COTK.Launcher.*", SearchOption.TopDirectoryOnly))
            File.Copy(runtimeFile, Path.Combine(updaterDirectory, Path.GetFileName(runtimeFile)), overwrite: true);
        var updaterExecutable = Path.Combine(updaterDirectory, Path.GetFileName(Environment.ProcessPath!));
        var plan = new LauncherUpdatePlan(
            archive,
            AppContext.BaseDirectory,
            Environment.ProcessPath!,
            Environment.ProcessId,
            updaterExecutable);
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        var psi = new ProcessStartInfo
        {
            FileName = updaterExecutable,
            WorkingDirectory = Path.GetDirectoryName(updaterExecutable)!,
            UseShellExecute = true,
        };
        psi.ArgumentList.Add("--apply-launcher-update");
        psi.ArgumentList.Add(planPath);
        _ = Process.Start(psi) ?? throw new UpdateException("Impossible de démarrer l'installateur du launcher.");
    }

    internal static async Task<int> ApplyLauncherUpdateAsync(string planPath)
    {
        LauncherUpdatePlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<LauncherUpdatePlan>(await File.ReadAllTextAsync(planPath), JsonOptions)
                ?? throw new JsonException("Plan vide.");
            ValidateArchivePath(plan.ArchivePath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or UpdateException)
        {
            return 1;
        }

        try
        {
            using var parent = Process.GetProcessById(plan.ParentProcessId);
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (ArgumentException) { }
        catch (TimeoutException) { return 1; }

        var stage = plan.TargetDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".update-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            ExtractZipSafely(plan.ArchivePath, stage);
            var sourceExe = Path.Combine(stage, Path.GetFileName(plan.LauncherExecutable));
            if (!File.Exists(sourceExe)) return 1;
            foreach (var source in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stage, source);
                var destination = Path.Combine(plan.TargetDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = plan.LauncherExecutable,
                WorkingDirectory = plan.TargetDirectory,
                UseShellExecute = true,
                Arguments = $"--cleanup-updater \"{plan.UpdaterExecutable}\"",
            });
            return 0;
        }
        catch { return 1; }
        finally
        {
            try { Directory.Delete(stage, true); } catch { }
            try { File.Delete(planPath); } catch { }
            try { File.Delete(plan.ArchivePath); } catch { }
        }
    }

    private async Task<string> DownloadVerifiedAsync(
        string url,
        string expectedSha256,
        long expectedSize,
        string name,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ValidateDownloadUrl(url);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && expectedSize > 0 && length != expectedSize)
            throw new UpdateException($"Taille inattendue pour l'archive {name}.");

        var path = Path.Combine(Path.GetTempPath(), $"cotk-{name}-{Guid.NewGuid():N}.zip");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 64, true);
        var buffer = new byte[1024 * 64];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (expectedSize > 0 && total > expectedSize) throw new UpdateException($"Archive {name} trop volumineuse.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report($"Téléchargement {name}: {(expectedSize > 0 ? total * 100 / expectedSize : 0)} %");
        }
        await output.FlushAsync(cancellationToken);
        if (expectedSize > 0 && total != expectedSize || !string.Equals(await Sha256Async(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new UpdateException($"Vérification SHA-256 échouée pour l'archive {name}.");
        }
        return path;
    }

    private static async Task InstallClientArchiveAsync(string archive, ClientRelease release, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var clientDir = GameLauncher.ClientDir;
        var stage = clientDir.TrimEnd(Path.DirectorySeparatorChar) + ".update-" + Guid.NewGuid().ToString("N");
        var backup = clientDir.TrimEnd(Path.DirectorySeparatorChar) + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            ExtractZipSafely(archive, stage);
            var stagedExe = Path.Combine(stage, release.Executable);
            if (!File.Exists(stagedExe))
                throw new UpdateException($"L'archive ne contient pas {release.Executable} à sa racine.");

            Directory.CreateDirectory(Path.GetDirectoryName(clientDir)!);
            if (Directory.Exists(clientDir))
                Directory.Move(clientDir, backup);
            Directory.Move(stage, clientDir);
            RestoreUserFiles(backup, clientDir);
            await File.WriteAllTextAsync(Path.Combine(clientDir, ".cotk-client-version"), release.Version + Environment.NewLine, cancellationToken);
            progress?.Report($"Client {release.Version} installé.");
        }
        catch
        {
            if (!Directory.Exists(clientDir) && Directory.Exists(backup)) Directory.Move(backup, clientDir);
            throw;
        }
        finally
        {
            try { Directory.Delete(stage, true); } catch { }
            try { Directory.Delete(backup, true); } catch { }
            try { File.Delete(archive); } catch { }
        }
    }

    private static void RestoreUserFiles(string backup, string clientDir)
    {
        if (!Directory.Exists(backup)) return;
        foreach (var relative in new[] { "ClientConfig.ini", "Logs" })
        {
            var source = Path.Combine(backup, relative);
            var destination = Path.Combine(clientDir, relative);
            if (File.Exists(source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            else if (Directory.Exists(source))
            {
                CopyDirectory(source, destination);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static void ValidateArchivePath(string path)
    {
        if (!Path.IsPathRooted(path) || !File.Exists(path) || !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("Archive locale invalide.");
    }

    private static void ExtractZipSafely(string archivePath, string destination)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("L'archive contient un chemin de sortie invalide.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}

internal sealed record UpdateResult(bool ManifestAvailable, bool RestartRequired, string Message, string? ClientVersion = null);
