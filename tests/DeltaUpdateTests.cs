using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace COTK.Launcher.Tests;

/// <summary>
/// Tests de regression du systeme de mise a jour (delta + archive complete).
/// Un serveur HTTP minimal embarque simule dl.cotk.fr : catalogue, fichiers
/// client et archives, avec coupures volontaires pour tester la reprise.
/// </summary>
public sealed class DeltaUpdateTests : IDisposable
{
    private readonly string _sandbox;
    private readonly string _clientDir;
    private readonly string? _originalClientDir;
    private readonly TestHttpServer _http;

    public DeltaUpdateTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "cotk-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _clientDir = Path.Combine(_sandbox, "client");
        _originalClientDir = LauncherSettings.ClientDirectory;
        // Redirige aussi le fichier de reglages persiste : sans cela, les tests
        // ecrasent %APPDATA%\COTK\settings.json et le launcher reel demarre
        // ensuite avec un dossier client inexistant (H1Z1.exe introuvable).
        Environment.SetEnvironmentVariable("COTK_SETTINGS_DIR", Path.Combine(_sandbox, "settings"));
        LauncherSettings.SetClientDirectory(_clientDir);
        _http = new TestHttpServer();
    }

    public void Dispose()
    {
        _http.Dispose();
        Environment.SetEnvironmentVariable("COTK_SETTINGS_DIR", null);
        LauncherSettings.ClientDirectory = _originalClientDir;
        try { Directory.Delete(_sandbox, true); } catch { }
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static byte[] ZipOf(params (string path, string content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return ms.ToArray();
    }

    // ------------------------------------------------------------------
    // 1. Mise a jour differentielle : modifie / manquant / superflu / preserve
    // ------------------------------------------------------------------
    [Fact]
    public async Task DeltaUpdate_RestoresChangedAndMissing_DeletesExtra_PreservesPlayerData()
    {
        // Etat "installe" : un client perime (marqueur 1.0.0) avec :
        // - same.txt  identique au catalogue   -> ne doit PAS etre retélécharge
        // - changed.txt corrompu               -> doit etre restaure
        // - missing.txt absent                 -> doit etre telecharge
        // - extra.txt absent du catalogue      -> doit etre supprime
        // - ClientConfig.ini + Logs/joueur.log -> doivent survivre intacts
        Directory.CreateDirectory(_clientDir);
        Directory.CreateDirectory(Path.Combine(_clientDir, "Logs"));
        File.WriteAllText(Path.Combine(_clientDir, "H1Z1.exe"), "old-exe");
        File.WriteAllText(Path.Combine(_clientDir, ".cotk-client-version"), "1.0.0");
        File.WriteAllText(Path.Combine(_clientDir, "same.txt"), "identique");
        File.WriteAllText(Path.Combine(_clientDir, "changed.txt"), "corrompu");
        File.WriteAllText(Path.Combine(_clientDir, "extra.txt"), "a-supprimer");
        File.WriteAllText(Path.Combine(_clientDir, "ClientConfig.ini"), "SessionId=jouer");
        File.WriteAllText(Path.Combine(_clientDir, "Logs", "joueur.log"), "precieux");
        var configBefore = File.ReadAllText(Path.Combine(_clientDir, "ClientConfig.ini"));

        var catalog = new
        {
            schemaVersion = 1,
            version = "2.0.0",
            baseUrl = $"{_http.BaseUrl}/client/",
            files = new[]
            {
                new { path = "H1Z1.exe", size = 7, sha256 = Sha256Of("new-exe") },
                new { path = "same.txt", size = 9, sha256 = Sha256Of("identique") },
                new { path = "changed.txt", size = 8, sha256 = Sha256Of("restaure") },
                new { path = "missing.txt", size = 8, sha256 = Sha256Of("present!") },
            },
        };
        _http.Map("/client-files.json", JsonSerializer.Serialize(catalog));
        _http.Map("/client/H1Z1.exe", "new-exe");
        _http.Map("/client/changed.txt", "restaure");
        _http.Map("/client/missing.txt", "present!");

        var release = new ClientRelease(
            "2.0.0",
            ArchiveUrl: $"{_http.BaseUrl}/client-full.zip",
            Sha256: new string('0', 64),
            SizeBytes: 0,
            FilesUrl: $"{_http.BaseUrl}/client-files.json");

        var service = new UpdateService();
        await service.EnsureClientAsync(release, progress: null, CancellationToken.None);

        Assert.Equal("restaure", File.ReadAllText(Path.Combine(_clientDir, "changed.txt")));
        Assert.Equal("present!", File.ReadAllText(Path.Combine(_clientDir, "missing.txt")));
        Assert.Equal("identique", File.ReadAllText(Path.Combine(_clientDir, "same.txt")));
        Assert.False(File.Exists(Path.Combine(_clientDir, "extra.txt")), "extra.txt devait etre supprime");
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(_clientDir, "H1Z1.exe")));
        Assert.Equal("2.0.0", File.ReadAllText(Path.Combine(_clientDir, ".cotk-client-version")).Trim());
        Assert.Equal(configBefore, File.ReadAllText(Path.Combine(_clientDir, "ClientConfig.ini")));
        Assert.Equal("precieux", File.ReadAllText(Path.Combine(_clientDir, "Logs", "joueur.log")));
        Assert.Equal(1, _http.RequestsFor("/client/changed.txt"));
        Assert.Equal(0, _http.RequestsFor("/client/same.txt"));
        Assert.Equal(0, _http.RequestsFor("/client/ClientConfig.ini"));
        Assert.Equal(0, _http.RequestsFor("/client/Logs/joueur.log"));
    }

    // ------------------------------------------------------------------
    // 2. Regression 416 : un .part complet ne doit generer AUCUNE requete
    //    (c'etait la boucle "Requested Range Not Satisfiable" infinie).
    // ------------------------------------------------------------------
    [Fact]
    public async Task FreshInstall_FullArchive_WritesMarkerAndRestoresNothing()
    {
        var zip = ZipOf(("H1Z1.exe", "brand-new"), ("ClientConfig.ini", "neuf"));
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        _http.Map("/client-full.zip", zip);

        var release = new ClientRelease(
            "1.0.0",
            ArchiveUrl: $"{_http.BaseUrl}/client-full.zip",
            Sha256: sha,
            SizeBytes: zip.Length);

        var service = new UpdateService();
        await service.EnsureClientAsync(release, progress: null, CancellationToken.None);

        Assert.Equal("brand-new", File.ReadAllText(Path.Combine(_clientDir, "H1Z1.exe")));
        Assert.Equal("1.0.0", File.ReadAllText(Path.Combine(_clientDir, ".cotk-client-version")).Trim());
    }

    // ------------------------------------------------------------------
    // 3. Installation fraiche via archive complete : extraction + marqueur.
    // ------------------------------------------------------------------
    [Fact]
    public async Task DownloadVerifiedAsync_CompletePartFile_MakesNoRequest()
    {
        var payload = "zip-complete-0123456789abcdef";
        var sha = Sha256Of(payload);
        var partPath = Path.Combine(Path.GetTempPath(), $"cotk-client-{sha[..12]}.part");
        File.WriteAllText(partPath, payload);
        try
        {
        var service = new UpdateService();
        var result = await service.DownloadVerifiedAsync(
            $"{_http.BaseUrl}/never-requested.zip", sha, payload.Length, "client", progress: null, CancellationToken.None);

            Assert.Equal(partPath, result);
            Assert.Equal(0, _http.TotalRequests);
        }
        finally
        {
            File.Delete(partPath);
        }
    }

    // ------------------------------------------------------------------
    // 4. Reprise : une premiere tentative tronquee doit etre completee par
    //    une requete Range (206), sans repartir de zero.
    // ------------------------------------------------------------------
    [Fact]
    public async Task DownloadVerifiedAsync_ResumesTruncatedDownloadWithRange()
    {
        var payload = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; // 26 octets
        var sha = Sha256Of(payload);
        _http.ServeTruncatedThenRange(payload);

        var service = new UpdateService();
        var result = await service.DownloadVerifiedAsync(
            $"{_http.BaseUrl}/big.zip", sha, payload.Length, "client", progress: null, CancellationToken.None);

        Assert.Equal(payload, File.ReadAllText(result));
        File.Delete(result);
    }

    private static string Sha256Of(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

/// <summary>Serveur HTTP minimal sur 127.0.0.1 : reponses mappees + mode
/// "tronque puis Range" pour tester la reprise de telechargement.</summary>
internal sealed class TestHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Dictionary<string, byte[]> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _requests = new(StringComparer.OrdinalIgnoreCase);
    private byte[]? _truncatedPayload;
    private int _truncatedHits;

    public string BaseUrl { get; }
    public int TotalRequests => _requests.Values.Sum();

    public TestHttpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = $"http://127.0.0.1:{port}";
        Task.Run(AcceptLoop);
    }

    public void Map(string path, string content) => Map(path, Encoding.UTF8.GetBytes(content));
    public void Map(string path, byte[] content) => _routes[path] = content;

    public int RequestsFor(string path) => _requests.TryGetValue(path, out var n) ? n : 0;

    public void ServeTruncatedThenRange(string payload) => _truncatedPayload = Encoding.UTF8.GetBytes(payload);

    private async Task AcceptLoop()
    {
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync();
                if (requestLine is null) continue;
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) continue;
                var method = parts[0];
                var rawUrl = parts[1];

                string? header;
                string? range = null;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
                {
                    if (header.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                        range = header["Range:".Length..].Trim();
                }

                _requests[rawUrl] = _requests.TryGetValue(rawUrl, out var n) ? n + 1 : 1;

                if (_truncatedPayload is not null && rawUrl.EndsWith("/big.zip"))
                {
                    ServeBigZip(stream, method, range);
                    continue;
                }

                if (method != "GET" || !_routes.TryGetValue(rawUrl, out var body))
                {
                    WriteResponse(stream, "404 Not Found", Array.Empty<byte>());
                    continue;
                }
                WriteResponse(stream, "200 OK", body);
            }
        }
        catch
        {
            // Socket arretee avec le test : silencieux.
        }
    }

    private static void WriteResponse(NetworkStream stream, string status, byte[] body)
    {
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        stream.Write(headers);
        stream.Write(body);
    }

    /// <summary>1re requete : renvoie la moitie puis coupe brut (simule une
    /// coupure). Suivantes avec Range : 206 avec le reste.</summary>
    private void ServeBigZip(NetworkStream stream, string method, string? range)
    {
        var payload = _truncatedPayload!;
        if (method != "GET")
        {
            WriteResponse(stream, "405 Method Not Allowed", Array.Empty<byte>());
            return;
        }

        if (range is null && _truncatedHits == 0)
        {
            _truncatedHits++;
            var half = payload[..(payload.Length / 2)];
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            stream.Write(headers);
            stream.Write(half);
            // Pas de fermeture propre : le flux meurt apres la moitie.
            return;
        }

        long offset = 0;
        if (range is not null)
        {
            var spec = range.Replace("bytes=", "");
            var first = spec.Split('-')[0];
            if (long.TryParse(first, out var parsed)) offset = parsed;
        }

        if (offset >= payload.Length)
        {
            WriteResponse(stream, "416 Requested Range Not Satisfiable", Array.Empty<byte>());
            return;
        }

        var rest = payload[(int)offset..];
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 206 Partial Content\r\nContent-Length: {rest.Length}\r\nConnection: close\r\n\r\n");
        stream.Write(head);
        stream.Write(rest);
    }

    public void Dispose() => _listener.Stop();
}
