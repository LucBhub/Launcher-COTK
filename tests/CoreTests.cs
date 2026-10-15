using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using COTK.Next.Core;

namespace COTK.Next.Tests;

/// <summary>Tests du noyau (sans framework : machine sans SDK). Sortie 0 = tout est vert.
/// Lancer : powershell -File tests\run-tests.ps1</summary>
internal static class CoreTests
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static int Main(string[] args)
    {
        var temp = Path.Combine(Path.GetTempPath(), "cotk-next-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Environment.SetEnvironmentVariable("COTK_SETTINGS_DIR", Path.Combine(temp, "settings"));
        Environment.SetEnvironmentVariable("COTK_LOG_DIR", Path.Combine(temp, "logs"));
        Environment.SetEnvironmentVariable("COTK_CLIENT_LOG_LEVEL", null);

        try
        {
            Run("log: secrets masqués", LogScrub);
            Run("config: client log level + doublons", ClientConfigBuilding);
            Run("jeu: détection d'étape (marqueurs réels)", StageDetection);
            Run("jeu: ticket de relogin postérieur au dernier 'en jeu'", ReloginDetection);
            Run("manifeste: chemins sûrs / données joueur", SafePaths);
            Run("manifeste: URLs (HTTPS, hôte cotk.fr, boucle locale)", UrlRules);
            Run("config: l'environnement ne peut pas viser un hôte distant", ConfigLocksProd);
            Run("erreurs: statut HTTP → code stable", ErrorMapping);
            Run("erreurs: socket → hors-ligne, TLS → certificat", ErrorExceptions);
            Run("formats: tailles, durées, dates relatives", Formats);
            Run("débit: vitesse lissée + temps restant", ThroughputMath);
            Run("empreintes: cache aller-retour + SHA-256 en flux", HashCaching);
            Run("zip: zip-slip refusé", ZipSlip);
            Run("réglages: clés inconnues conservées, BOM toléré", SettingsRoundTrip);
            Run("installateur: delta (reprise, atomique, nettoyage APRÈS succès)", () => DeltaScenario(temp));
            Run("installateur: 404 → rien n'est supprimé, erreur claire", () => DeltaMissingFile(temp));
            Run("installateur: repli archive confirmé une seule fois + réglages préservés", () => FullFallback(temp));
            Run("installateur: catalogue d'une autre version refusé sans téléchargement", () => WrongCatalog(temp));
            Run("installateur: espace disque insuffisant détecté avant d'écrire", DiskCheck);
            Run("classement: paliers et scores limites", RankFractions);
            Run("support: extrait expurgé, borné, consentement exigé", () => SupportExtract(temp));
            Run("langues : anglais par défaut, erreurs, variables, chinois et persistance", Localizations);
            Run("désinstallation : fichiers gérés supprimés, données joueur conservées",()=>UninstallTests.PreservesData(temp));
            Run("désinstallation : dossiers protégés, chemins invalides et installation inconnue refusés",()=>UninstallTests.RefusesUnsafe(temp));
            Run("désinstallation : jeu ouvert bloqué avant suppression",()=>UninstallTests.RefusesRunningGame(temp));
            Run("désinstallation : fichier verrouillé et reprise de l'opération",()=>UninstallTests.RetryLockedFile(temp));
            Run("désinstallation : jonction refusée sans toucher à sa cible",()=>UninstallTests.RefusesJunction(temp));
            Run("désinstallation : ancien client sans inventaire",()=>UninstallTests.LegacyCatalogue(temp));
            Run("désinstallation puis réinstallation : réglages et fichiers inconnus intacts",()=>UninstallReinstall(temp));
            Run("budget disque exact, reprise et lecture impossible bloquée",()=>StorageBudgets(temp));
            Run("précontrôle : fichiers essentiels, configuration et maintenance",()=>LaunchChecks(temp));
            Run("pause : attente sans activité, reprise et annulation",PauseTransfer);
            Run("publication signée : signature, expiration, produit, catalogue épinglé",SignedReleases);
            Run("console F8 staff : installation vérifiée, refus sans abîmer l'existant",()=>StaffConsoleInstall(temp));
            Run("mise à jour du launcher : détection, téléchargement vérifié",()=>LauncherUpdateDownload(temp));
            Run("mise à jour du launcher : remplacement, données du joueur intactes, relance",()=>LauncherUpdateApply(temp));
            Run("mise à jour du launcher : échec restauré, paquet dangereux refusé",()=>LauncherUpdateRollback(temp));
            Run("mise à jour du launcher : nettoyage limité à nos dossiers",()=>LauncherUpdateCleanup(temp));
            Run("client : réglages du joueur intacts (état laissé par l'ancien launcher, mise à jour, réparation)",()=>PlayerSettingsDelta(temp));
            Run("client : réglages du joueur intacts lors du repli sur l'archive complète",()=>PlayerSettingsFullArchive(temp));
            Run("client : même réglages, même dossier et même marqueur que l'ancien launcher",()=>OldLauncherCompatibility(temp));
            if (args.Contains("--api-smoke")) Run("API production : état, versions, actualités, classement et 401", ApiSmoke);
        }
        catch (Exception ex)
        {
            Failures.Add("FATAL " + ex);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine($"{_passed} OK, {Failures.Count} échec(s)");
        foreach (var f in Failures) Console.WriteLine("  ✗ " + f);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("  ✓ " + name); }
        catch (Exception ex) { Failures.Add($"{name} — {ex.Message}"); Console.WriteLine("  ✗ " + name + " — " + ex.Message); }
    }

    private static void Run(string name, Func<Task> test) => Run(name, () => test().GetAwaiter().GetResult());

    private static void True(bool condition, string what)
    {
        if (!condition) throw new Exception("attendu vrai : " + what);
    }

    private static void Eq<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what} : attendu <{expected}> obtenu <{actual}>");
    }

    // ------------------------------------------------------------------ tests purs

    private static void LogScrub()
    {
        var text = Log.Scrub("ticket lp2.abcdef12345.exp.sig Authorization: Bearer abcdefghijklmnop1234567890 \"password\":\"hunter2secret\"");
        True(!text.Contains("abcdef12345"), "ticket masqué");
        True(!text.Contains("abcdefghijklmnop1234567890"), "bearer masqué");
        True(!text.Contains("hunter2secret"), "mot de passe masqué");
        True(text.Contains("lp2.***"), "marqueur de ticket");
    }

    private static void ClientConfigBuilding()
    {
        var source = new[] { "World=None", " Server=192.168.1.10:20042", "SessionId=old", "  sessionid=older", "[Logging]", "Address=", "LocalLogLevel=9", " localloglevel=7", "[Environment]", "Sku=2" };
        var result = GameFiles.BuildClientConfig(source, "lp2.t", "164.132.200.95:20042");
        Eq("SessionId=lp2.t", result[0], "ticket en tête");
        Eq("Server=164.132.200.95:20042", result[1], "serveur en second");
        Eq(1, result.Count(l => l.TrimStart().StartsWith("SessionId=", StringComparison.OrdinalIgnoreCase)), "une seule session");
        Eq(1, result.Count(l => l.TrimStart().StartsWith("LocalLogLevel=", StringComparison.OrdinalIgnoreCase)), "un seul niveau de log");
        Eq("LocalLogLevel=4", result[result.IndexOf("[Logging]") + 1], "niveau 4 sous [Logging]");
        True(result.Contains("Address=") && result.Contains("Sku=2"), "autres lignes intactes");

        var bare = GameFiles.BuildClientConfig(new[] { "World=None", "[Environment]", "Sku=2" }, "lp2.t", "x:1");
        True(bare.IndexOf("[Logging]") > 0 && bare[bare.IndexOf("[Logging]") + 1] == "LocalLogLevel=4", "section ajoutée");
    }

    private static void StageDetection()
    {
        var cases = new (string log, ClientStage expected)[]
        {
            ("BaseApp::Run - beginning", ClientStage.Starting),
            ("newState=GAMESTATE_TITLESCREEN", ClientStage.TitleScreen),
            ("newState=cClientRunStateCharacterCreateOrDelete", ClientStage.TitleScreen),
            ("newState=GAMESTATE_LOADINGSCREEN", ClientStage.LoadingWorld),
            ("WaitForWorldReady: Waiting", ClientStage.LoadingWorld),
            ("newState=cClientRunStateRunning", ClientStage.InGame),
            ("newState=cClientRunStateRunning\nnewState=GAMESTATE_LOADINGSCREEN", ClientStage.LoadingWorld),
            ("newState=GAMESTATE_LOADINGSCREEN\nnewState=cClientRunStateRunning", ClientStage.InGame),
            ("WaitForWorldReady: a\nnewState=GAMESTATE_TITLESCREEN\nWaitForWorldReady: b", ClientStage.LoadingWorld),
            ("newState=GAMESTATE_TITLESCREEN\nWaitForWorldReady: Waiting\nnewState=GAMESTATE_INGAME", ClientStage.InGame),
            ("", ClientStage.Starting),
        };
        foreach (var (log, expected) in cases)
            Eq(expected, GameFiles.DetectClientStage(log), "étape de « " + log.Replace("\n", " | ") + " »");
    }

    private static void ReloginDetection()
    {
        True(!GameFiles.NeedsReloginTicket("newState=GAMESTATE_INGAME", out _), "pas de marqueur");
        True(GameFiles.NeedsReloginTicket("newState=GAMESTATE_INGAME\nWaitingForReloginSession", out var p) && p > 0, "marqueur après le jeu");
        True(!GameFiles.NeedsReloginTicket("WaitingForReloginSession\nnewState=GAMESTATE_INGAME", out _), "vieux marqueur avant un nouveau jeu");
    }

    private static void SafePaths()
    {
        foreach (var good in new[] { "H1Z1.exe", "Assets/Assets_256.pack", "UI\\Settings\\a.xml" })
            True(ManifestRules.IsSafeRelative(good), "sûr : " + good);
        foreach (var bad in new[] { "", "../x", "a/../b", "/abs", "C:\\x", "a/./b", "a//b", "evil.exe ", "a:b", "..\\..\\Windows\\x", "CON", "a/NUL.txt", "LPT1.exe", "a/*.pack" })
            True(!ManifestRules.IsSafeRelative(bad), "refusé : '" + bad + "'");

        True(ManifestRules.IsPlayerData("UserOptions.ini"), "ini racine");
        True(ManifestRules.IsPlayerData("InputProfile_Custom.xml"), "profil de touches");
        True(ManifestRules.IsPlayerData("UI/Settings/x.xml"), "réglages UI");
        True(ManifestRules.IsPlayerData(".cotk-hash-cache.json"), "cache d'empreintes");
        True(!ManifestRules.IsPlayerData("Assets/Assets_1.pack"), "pack = pas une donnée joueur");
        True(!ManifestRules.IsPlayerData("sub/config.ini"), "ini hors racine = fichier du jeu");
        True(ManifestRules.IsNewer("1.2.0", "1.1.9") && !ManifestRules.IsNewer("1.0.0", "1.0.0") && !ManifestRules.IsNewer("x", "1.0.0"), "IsNewer");
    }

    private static void UrlRules()
    {
        ManifestRules.ValidateUrl("https://dl.cotk.fr/a.zip");
        ManifestRules.ValidateUrl("https://api.cotk.fr/x");
        foreach (var bad in new[] { "http://dl.cotk.fr/a.zip", "https://evil.example.com/a.zip", "https://cotk.fr.evil.com/a", "https://user:pw@dl.cotk.fr/a", "ftp://dl.cotk.fr/a", "relatif/a.zip", "http://127.0.0.1:8080/a" })
        {
            try { ManifestRules.ValidateUrl(bad); throw new Exception("accepté à tort : " + bad); }
            catch (LauncherException) { }
        }
    }

    private static void ConfigLocksProd()
    {
        Environment.SetEnvironmentVariable("COTK_API_URL", "https://evil.example.com");
        Environment.SetEnvironmentVariable("COTK_GAME_SERVER", "6.6.6.6:20042");
        try
        {
            Eq(Config.ProdApi, Config.ApiUrl, "API distante ignorée");
            Eq(Config.ProdGameServer, Config.GameServer, "serveur distant ignoré");
            Environment.SetEnvironmentVariable("COTK_API_URL", "http://localhost:8080");
            Environment.SetEnvironmentVariable("COTK_GAME_SERVER", "127.0.0.1:20042");
            Eq("http://localhost:8080", Config.ApiUrl, "API locale acceptée");
            True(Config.IsLocalGameServer, "serveur local");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COTK_API_URL", null);
            Environment.SetEnvironmentVariable("COTK_GAME_SERVER", null);
        }
    }

    private static void ErrorMapping()
    {
        Eq("E-AUTH-001", Errors.FromStatus(401, "invalid credentials", null).Code, "401 identifiants");
        Eq("E-AUTH-003", Errors.FromStatus(401, "authentication required", null).Code, "401 session");
        Eq("E-AUTH-002", Errors.FromStatus(403, "account banned", null).Code, "403 banni");
        Eq("E-NET-004", Errors.FromStatus(429, null, TimeSpan.FromSeconds(12)).Code, "429");
        True(Errors.FromStatus(429, null, TimeSpan.FromSeconds(12)).Message.Contains("12"), "429 indique le délai");
        Eq("E-NET-005", Errors.FromStatus(503, null, null).Code, "5xx");
        Eq("E-AUTH-004", Errors.FromStatus(400, "invalid or expired device code", null).Code, "code appareil expiré");
        Eq("E-NET-006", Errors.FromStatus(418, "x", null).Code, "inconnu");
        True(Errors.Offline().Report().Contains("E-NET-001"), "rapport lisible avec le code");
    }

    private static void ErrorExceptions()
    {
        Eq("E-NET-001", Errors.From(new HttpRequestException("x", new SocketException())).Code, "socket");
        Eq("E-NET-003", Errors.From(new HttpRequestException("x", new System.Security.Authentication.AuthenticationException("cert"))).Code, "tls");
        Eq("E-NET-002", Errors.From(new TaskCanceledException()).Code, "timeout");
        Eq("E-DISK-002", Errors.From(new UnauthorizedAccessException("no")).Code, "accès refusé");
        Eq("E-APP-001", Errors.From(new InvalidOperationException("boom")).Code, "inattendu");
    }

    private static void Formats()
    {
        Eq("1,0 Go", Fmt.Size(1L << 30), "Go");
        Eq("2,5 Mo", Fmt.Size((long)(2.5 * (1 << 20))), "Mo");
        Eq("512 o", Fmt.Size(512), "o");
        Eq("1 min 05 s", Fmt.Eta(TimeSpan.FromSeconds(65)), "eta min");
        Eq("2 h 03 min", Fmt.Eta(TimeSpan.FromMinutes(123)), "eta h");
        Eq("—", Fmt.Eta(null), "eta inconnue");
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        Eq("À l'instant", Fmt.Relative(now.AddSeconds(-30), now), "instant");
        Eq("Il y a 3 h", Fmt.Relative(now.AddHours(-3), now), "heures");
        Eq("Hier", Fmt.Relative(now.AddHours(-30), now), "hier");
        Eq("Il y a 5 jours", Fmt.Relative(now.AddDays(-5), now), "jours");
    }

    private static void ThroughputMath()
    {
        long clock = 0;
        var t = new Throughput(TimeSpan.FromSeconds(5), () => clock);
        for (var i = 0; i <= 10; i++)
        {
            clock = i * System.Diagnostics.Stopwatch.Frequency; // 1 s par pas
            t.Add(i * 1_000_000L);                                // 1 Mo/s
        }
        True(Math.Abs(t.BytesPerSecond - 1_000_000) < 50_000, "≈ 1 Mo/s (obtenu " + t.BytesPerSecond + ")");
        var eta = t.Eta(10_000_000);
        True(eta is { } e && Math.Abs(e.TotalSeconds - 10) < 1, "ETA ≈ 10 s");
    }

    private static void HashCaching()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cotk-hc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.bin");
            File.WriteAllBytes(file, Encoding.UTF8.GetBytes("hello"));
            var sha = Hashing.Sha256Async(file).GetAwaiter().GetResult();
            Eq("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", sha, "sha256(hello)");

            var cache = HashCache.Load(dir);
            var info = new FileInfo(file);
            True(!cache.TryGet("a.bin", info, out _), "cache vide");
            cache.Set("a.bin", info, sha);
            cache.Save();
            var reloaded = HashCache.Load(dir);
            True(reloaded.TryGet("a.bin", new FileInfo(file), out var cached) && cached == sha, "relu depuis le disque");
            File.AppendAllText(file, "!");
            True(!reloaded.TryGet("a.bin", new FileInfo(file), out _), "fichier modifié = cache invalide");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void ZipSlip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cotk-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zipPath = Path.Combine(dir, "evil.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var w = new StreamWriter(zip.CreateEntry("../../escaped.txt").Open());
                w.Write("pwn");
            }
            try
            {
                ClientInstaller.ExtractZipSafely(zipPath, Path.Combine(dir, "out"));
                throw new Exception("zip-slip non détecté");
            }
            catch (LauncherException ex) { Eq("E-UPD-004", ex.Error.Code, "code zip-slip"); }
            True(!File.Exists(Path.Combine(dir, "escaped.txt")), "rien écrit hors du dossier");

            try { ClientInstaller.LocalPath(Path.Combine(dir, "client"), "..\\evil.txt"); throw new Exception("LocalPath n'a pas refusé"); }
            catch (LauncherException) { }
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void SettingsRoundTrip()
    {
        Directory.CreateDirectory(AppPaths.SettingsDir);
        File.WriteAllText(AppPaths.SettingsFile, "\uFEFF{ \"clientDir\": \"D:\\\\Games\\\\client\", \"futureKey\": 42 }");
        Settings.Load();
        Eq("D:\\Games\\client", Settings.ClientDirectory, "clientDir lu malgré le BOM");
        Settings.MinimizeOnPlay = false;
        var saved = File.ReadAllText(AppPaths.SettingsFile);
        True(saved.Contains("futureKey") && saved.Contains("minimizeOnPlay"), "clé inconnue conservée + nouvelle clé écrite");
        File.WriteAllText(AppPaths.SettingsFile, "{ not json");
        Settings.Load();
        True(Settings.ClientDirectory is null, "fichier corrompu = défauts");
    }

    private static void DiskCheck()
    {
        try
        {
            ClientInstaller.EnsureDiskSpace(Path.GetTempPath(), long.MaxValue / 2);
            throw new Exception("espace insuffisant non détecté");
        }
        catch (LauncherException ex) { Eq("E-DISK-001", ex.Error.Code, "code disque"); }
        ClientInstaller.EnsureDiskSpace(Path.GetTempPath(), 1024);
    }

    // ------------------------------------------------------------------ installateur contre un vrai serveur HTTP local

    private sealed class FakeCdn : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Dictionary<string, byte[]> _files = new();
        private readonly CancellationTokenSource _stop = new();
        public int Port { get; }
        public HashSet<string> Missing { get; } = new();
        public int RangeRequests;

        public FakeCdn()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        public string BaseUrl => $"http://127.0.0.1:{Port}";
        public void Add(string path, byte[] data) => _files["/" + path] = data;

        private async Task Loop()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch { return; }
                _ = Task.Run(() => Serve(ctx));
            }
        }

        private void Serve(HttpListenerContext ctx)
        {
            try
            {
                var path = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath);
                if (Missing.Contains(path) || !_files.TryGetValue(path, out var data))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                var range = ctx.Request.Headers["Range"];
                long start = 0;
                if (range is not null && range.StartsWith("bytes=") && long.TryParse(range[6..].TrimEnd('-'), out start))
                {
                    Interlocked.Increment(ref RangeRequests);
                    ctx.Response.StatusCode = 206;
                }
                var slice = data.AsSpan((int)start).ToArray();
                ctx.Response.ContentLength64 = slice.Length;
                ctx.Response.OutputStream.Write(slice, 0, slice.Length);
                ctx.Response.Close();
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }

        public void Dispose() { _stop.Cancel(); try { _listener.Close(); } catch { } }
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static (FakeCdn cdn, string clientDir, ClientRelease release) Prepare(string root, string name, Dictionary<string, byte[]> remote)
    {
        var cdn = new FakeCdn();
        Environment.SetEnvironmentVariable("COTK_API_URL", cdn.BaseUrl);

        var catalog = new
        {
            schemaVersion = 1,
            version = "9.9.9.9",
            fileCount = remote.Count,
            totalBytes = remote.Values.Sum(v => (long)v.Length),
            baseUrl = cdn.BaseUrl + "/client/",
            files = remote.Select(kv => new { path = kv.Key, size = (long)kv.Value.Length, sha256 = Sha(kv.Value) }).ToArray(),
        };
        cdn.Add("client-files.json", Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(catalog)));
        foreach (var (path, data) in remote) cdn.Add("client/" + path, data);

        var clientDir = Path.Combine(root, name, "client");
        Directory.CreateDirectory(clientDir);
        Settings.ClientDirectory = clientDir;
        var release = new ClientRelease("9.9.9.9", cdn.BaseUrl + "/client.zip", new string('a', 64), 1, "H1Z1.exe", true, cdn.BaseUrl + "/client-files.json");
        return (cdn, clientDir, release);
    }

    private sealed class Sink : IProgress<TransferProgress>
    {
        public readonly List<TransferProgress> Items = new();
        public void Report(TransferProgress value) { lock (Items) Items.Add(value); }
    }

    private static async Task DeltaScenario(string root)
    {
        var exe = new byte[2000]; new Random(1).NextBytes(exe);
        var pack = new byte[400_000]; new Random(2).NextBytes(pack);
        var small = Encoding.UTF8.GetBytes("nouveau");
        var (cdn, dir, release) = Prepare(root, "delta", new()
        {
            ["H1Z1.exe"] = exe,
            ["Assets/Assets_1.pack"] = pack,
            ["Assets/new file é.dat"] = small,
        });
        using var _ = cdn;
        try
        {
            // état local : exe identique, pack tronqué (reprise), un fichier obsolète, des réglages joueur
            Directory.CreateDirectory(Path.Combine(dir, "Assets"));
            File.WriteAllBytes(Path.Combine(dir, "H1Z1.exe"), exe);
            File.WriteAllBytes(Path.Combine(dir, "Assets", "Assets_1.pack"), pack.AsSpan(0, 1000).ToArray()); // taille différente
            File.WriteAllBytes(Path.Combine(dir, "Assets", "Assets_1.pack.cotk-part"), pack.AsSpan(0, 150_000).ToArray()); // partiel à reprendre
            File.WriteAllText(Path.Combine(dir, "Assets", "old.pack"), "obsolete");
            File.WriteAllText(Path.Combine(dir, ".cotk-managed-files.json"), "[\"Assets/old.pack\"]");
            File.WriteAllText(Path.Combine(dir, "my-screenshot.dat"), "personal");
            File.WriteAllText(Path.Combine(dir, "UserOptions.ini"), "mes=reglages");
            File.WriteAllText(Path.Combine(dir, ".cotk-client-version"), "0.0.1");

            var progress = new Sink();
            var result = await new ClientInstaller().EnsureAsync(release, progress, CancellationToken.None);

            True(result is { FullArchive: false, WasUpdate: true }, "chemin différentiel");
            Eq(2, result.FilesDownloaded, "2 fichiers téléchargés (pack + nouveau)");
            True(File.ReadAllBytes(Path.Combine(dir, "Assets", "Assets_1.pack")).SequenceEqual(pack), "pack identique au serveur");
            True(File.ReadAllText(Path.Combine(dir, "Assets", "new file é.dat")) == "nouveau", "nom avec espace/accents téléchargé");
            True(!File.Exists(Path.Combine(dir, "Assets", "old.pack")), "fichier obsolète supprimé APRÈS succès");
            Eq("personal", File.ReadAllText(Path.Combine(dir, "my-screenshot.dat")), "fichier inconnu du joueur conservé");
            True(!File.Exists(Path.Combine(dir, "Assets", "Assets_1.pack.cotk-part")), "plus de .cotk-part");
            Eq("mes=reglages", File.ReadAllText(Path.Combine(dir, "UserOptions.ini")), "réglages joueur intacts");
            Eq("9.9.9.9", ClientInstaller.InstalledVersion(), "marqueur de version");
            True(cdn.RangeRequests >= 1, "reprise Range utilisée");
            True(progress.Items.Any(p => p.Phase == TransferPhase.Downloading && p.BytesPerSecond >= 0 && p.BytesTotal > 0), "progression d'octets émise");
            True(progress.Items.Last().Phase == TransferPhase.Done, "dernière étape = terminé");

            // 2e passage : tout est à jour, le cache évite de relire les fichiers
            var second = await new ClientInstaller().VerifyAsync(release, repair: false, new Sink(), CancellationToken.None);
            Eq(0, second.FilesDownloaded, "rien à retélécharger");
            True(File.Exists(Path.Combine(dir, ".cotk-hash-cache.json")), "cache d'empreintes écrit");

            // réparation : un fichier corrompu à taille identique est détecté par SHA et refait
            var corrupt = (byte[])pack.Clone(); corrupt[10] ^= 0xFF;
            File.WriteAllBytes(Path.Combine(dir, "Assets", "Assets_1.pack"), corrupt);
            var repaired = await new ClientInstaller().VerifyAsync(release, repair: true, new Sink(), CancellationToken.None);
            Eq(1, repaired.FilesDownloaded, "fichier corrompu réparé");
            True(File.ReadAllBytes(Path.Combine(dir, "Assets", "Assets_1.pack")).SequenceEqual(pack), "contenu réparé");
        }
        finally { Environment.SetEnvironmentVariable("COTK_API_URL", null); }
    }

    private static async Task DeltaMissingFile(string root)
    {
        var exe = new byte[1000]; new Random(3).NextBytes(exe);
        var data = new byte[5000]; new Random(4).NextBytes(data);
        var (cdn, dir, release) = Prepare(root, "missing", new() { ["H1Z1.exe"] = exe, ["Assets/b.pack"] = data });
        using var _ = cdn;
        try
        {
            cdn.Missing.Add("/client/Assets/b.pack");
            File.WriteAllBytes(Path.Combine(dir, "H1Z1.exe"), exe);
            File.WriteAllText(Path.Combine(dir, "leftover.pack"), "garde-moi");
            File.WriteAllText(Path.Combine(dir, ".cotk-client-version"), "0.0.1");

            var installer = new ClientInstaller { ConfirmFullDownload = (_, _) => Task.FromResult(false) };
            try
            {
                await installer.EnsureAsync(release, new Sink(), CancellationToken.None);
                throw new Exception("l'échec aurait dû remonter");
            }
            catch (LauncherException ex) { Eq("E-UPD-003", ex.Error.Code, "fichier introuvable sur le serveur"); }

            True(File.Exists(Path.Combine(dir, "leftover.pack")), "AUCUN fichier supprimé quand la mise à jour échoue");
            Eq("0.0.1", ClientInstaller.InstalledVersion(), "version inchangée après échec");
        }
        finally { Environment.SetEnvironmentVariable("COTK_API_URL", null); }
    }

    private static void RankFractions()
    {
        var t = RankThresholds.Default;
        Eq(0.0, RankMath.TrackFraction(9999999, 9, t), "pas de rang avant 10 matchs");
        Eq(0.2, RankMath.TrackFraction(t.Silver, 10, t), "seuil Argent");
        Eq(0.6, RankMath.TrackFraction(t.Platinum, 10, t), "seuil Platine");
        Eq(1.0, RankMath.TrackFraction(t.Royalty, 10, t), "seuil maximal");
        True(RankMath.Next(t.Royalty, 6, t) is null, "pas de palier après Royauté");
    }

    private static void SupportExtract(string root)
    {
        var log = Path.Combine(root, "support.log");
        var lines = Enumerable.Range(0, 3000).Select(i => $"line {i} user alice@example.com 192.168.0.4 C:\\Users\\Alice\\x password=hunter2 SessionId=lp2.abc.def.ghi Bearer abcdefghijklmnop");
        File.WriteAllLines(log, lines);
        var text = SupportReport.ReadTail(log, 50);
        True(text.Length <= 6000 + 40, "extrait borné (" + text.Length + ")");
        foreach (var secret in new[] { "alice@example.com", "192.168.0.4", "Alice", "hunter2", "lp2.abc", "abcdefghijklmnop" })
            True(!text.Contains(secret), "secret masqué : " + secret);
        True(text.Contains("line 2999"), "fin du journal conservée");
        True(!text.Contains("line 0 "), "début du journal non lu");
        var report = SupportReport.Prepare(10, false, "0.1", "E-TEST");
        True(!report.Consent, "pas de consentement implicite");
        try { new ApiClient().SubmitReportAsync("t", report, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("envoi sans consentement accepté"); }
        catch (LauncherException ex) { Eq("E-SUPPORT-001", ex.Error.Code, "consentement"); }
    }

    private static async Task ApiSmoke()
    {
        Environment.SetEnvironmentVariable("COTK_API_URL", null);
        using var api = new ApiClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        True(await api.PingAsync(ct) is > 0, "API répond");
        var manifest = await api.GetManifestAsync(ct);
        True(manifest is not null, "manifeste présent");
        ManifestRules.Validate(manifest!);
        True((await api.GetPatchNotesAsync(3, ct)).Count > 0, "actualités disponibles");
        True((await api.GetLeaderboardAsync(3, ct)).Entries.Count > 0, "classement disponible");
        try { await api.GetAccountAsync("invalid-qa-token", ct); throw new Exception("jeton invalide accepté"); }
        catch (LauncherException ex) { Eq("E-AUTH-003", ex.Error.Code, "session invalide correctement expliquée"); }
        Console.WriteLine($"  Production : launcher {manifest!.Launcher.Version}, client {manifest.Client.Version}");
    }

    private static void Localizations()
    {
        Settings.Load();
        L.Initialize();
        Eq("en", L.Language, "anglais par défaut");
        Eq("Sign in",L.T("Se connecter"),"connexion anglaise");
        Eq("PLAY",L.T("JOUER"),"bouton en capitales");
        Eq("59.2 MB/s · 3 min 11 s",L.T("59,2 Mo/s · 3 min 11 s"),"unités d'un transfert traduites");
        Eq("Try again in 12 seconds.",L.T("Réessayez dans 12 secondes."),"variable d'erreur préservée");
        Eq("Integrity verification failed for: Assets/a.pack.",L.T("La vérification d'intégrité a échoué pour : Assets/a.pack."),"chemin technique préservé");
        True(L.T(Errors.BadCredentials().Report()).StartsWith("[E-AUTH-001] Incorrect credentials"),"rapport localisé sans modifier le code d'erreur");
        Eq("IronWolf_92",L.T("IronWolf_92"),"identifiant inchangé");
        L.Select("zh-CN");
        Eq("登录",L.T("Se connecter"),"connexion chinoise");
        Eq("请在 12 秒后重试。",L.T("Réessayez dans 12 secondes."),"variable chinoise");
        Eq("59.2 MB/s · 3 分钟 11 秒",L.T("59,2 Mo/s · 3 min 11 s"),"durée chinoise");
        True(L.T(UiSpaced("Téléchargement")).Contains("下载"),"capitales aérées traduites");
        Eq("FR",L.T("FR"),"code de langue inchangé");
        Settings.Load(); L.Initialize();
        Eq("zh-CN",L.Language,"langue relue depuis le disque");
        L.Select("fr"); Eq("Se connecter",L.T("Se connecter"),"français intact");
        L.Initialize("unsupported"); Eq("en",L.Language,"langue invalide : repli anglais");
        L.Select("en");
        Eq("en",Settings.Language,"choix explicite enregistré même après une surcharge");
    }

    private static string UiSpaced(string value) => string.Join('\u200A',value.ToUpperInvariant().Select(c=>c.ToString()));

    private static async Task FullFallback(string root)
    {
        var exe = Encoding.UTF8.GetBytes("nouvel executable de test");
        var pack = Encoding.UTF8.GetBytes("nouveau pack");
        var (cdn, dir, release) = Prepare(root, "fallback", new() { ["H1Z1.exe"] = exe, ["Assets/b.pack"] = pack });
        using var _ = cdn;
        try
        {
            File.WriteAllText(Path.Combine(dir, "H1Z1.exe"), "old");
            File.WriteAllText(Path.Combine(dir, "UserOptions.ini"), "personal=settings");
            File.WriteAllText(Path.Combine(dir, ".cotk-client-version"), "0.0.1");
            cdn.Missing.Add("/client/Assets/b.pack");
            using var buffer = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var stream = zip.CreateEntry("H1Z1.exe").Open()) stream.Write(exe);
                using (var stream = zip.CreateEntry("Assets/b.pack").Open()) stream.Write(pack);
                using var writer = new StreamWriter(zip.CreateEntry("UserOptions.ini").Open());
                writer.Write("default=settings");
            }
            var archive = buffer.ToArray();
            cdn.Add("client.zip", archive);
            release = release with { Sha256 = Sha(archive), SizeBytes = archive.Length };
            var confirmations = 0;
            var installer = new ClientInstaller { ConfirmFullDownload = (_, _) => { confirmations++; return Task.FromResult(true); } };
            var result = await installer.EnsureAsync(release, new Sink(), CancellationToken.None);
            True(result.FullArchive, "archive complète installée");
            Eq(1, confirmations, "une confirmation seulement");
            Eq("personal=settings", File.ReadAllText(Path.Combine(dir, "UserOptions.ini")), "réglages intacts après swap");
            True(File.ReadAllBytes(Path.Combine(dir, "H1Z1.exe")).SequenceEqual(exe), "exécutable installé");
        }
        finally { Environment.SetEnvironmentVariable("COTK_API_URL", null); }
    }

    private static async Task WrongCatalog(string root)
    {
        var (cdn, dir, release) = Prepare(root, "wrong-version", new() { ["H1Z1.exe"] = Encoding.UTF8.GetBytes("remote") });
        using var _ = cdn;
        try
        {
            File.WriteAllText(Path.Combine(dir, "H1Z1.exe"), "local");
            var confirmations = 0;
            var installer = new ClientInstaller { ConfirmFullDownload = (_, _) => { confirmations++; return Task.FromResult(true); } };
            try { await installer.EnsureAsync(release with { Version = "10.0.0" }, new Sink(), CancellationToken.None); throw new Exception("catalogue incompatible accepté"); }
            catch (LauncherException ex) { Eq("E-UPD-004", ex.Error.Code, "mise à jour incompatible refusée"); }
            Eq(0, confirmations, "pas de repli archive après un refus de sécurité");
            Eq("local", File.ReadAllText(Path.Combine(dir, "H1Z1.exe")), "aucune altération");
        }
        finally { Environment.SetEnvironmentVariable("COTK_API_URL", null); }
    }

    private static async Task UninstallReinstall(string root)
    {
        var exe=Encoding.UTF8.GetBytes("test executable"); var pack=Encoding.UTF8.GetBytes("test pack");
        var(cdn,dir,release)=Prepare(root,"uninstall-roundtrip",new(){["H1Z1.exe"]=exe,["Assets/a.pack"]=pack});
        using var server=cdn;
        try
        {
            using var buffer=new MemoryStream();
            using(var zip=new System.IO.Compression.ZipArchive(buffer,System.IO.Compression.ZipArchiveMode.Create,true))
            {
                using(var stream=zip.CreateEntry("H1Z1.exe").Open()) stream.Write(exe);
                using(var stream=zip.CreateEntry("Assets/a.pack").Open()) stream.Write(pack);
                using var writer=new StreamWriter(zip.CreateEntry("UserOptions.ini").Open()); writer.Write("defaults");
            }
            var data=buffer.ToArray(); cdn.Add("client.zip",data);
            release=release with {Sha256=Sha(data),SizeBytes=data.Length};
            var installer=new ClientInstaller{ConfirmFullDownload=(_,_)=>Task.FromResult(true)};
            await installer.EnsureAsync(release,new Sink(),CancellationToken.None);
            True(File.Exists(Path.Combine(dir,".cotk-managed-files.json")),"inventaire installé dès l'archive complète");
            File.WriteAllText(Path.Combine(dir,"UserOptions.ini"),"my settings");
            File.WriteAllText(Path.Combine(dir,"personal.txt"),"my content");
            ClientUninstaller.Execute(ClientUninstaller.Prepare(dir),()=>false);
            True(!ClientInstaller.IsInstalled(release),"client non installé après suppression");
            await installer.EnsureAsync(release,new Sink(),CancellationToken.None);
            True(ClientInstaller.IsInstalled(release),"réinstallation dans le même dossier");
            Eq("my settings",File.ReadAllText(Path.Combine(dir,"UserOptions.ini")),"réglages restaurés");
            Eq("my content",File.ReadAllText(Path.Combine(dir,"personal.txt")),"fichier inconnu restauré");
            True(!File.Exists(Path.Combine(dir,ClientUninstaller.ReceiptName)),"ancien reçu non recopié");
        }
        finally { Environment.SetEnvironmentVariable("COTK_API_URL",null); }
    }

    private static void StorageBudgets(string root)
    {
        Eq(1000L+4000L+500L+StorageCheck.Margin,StorageCheck.Required(1000,4000,500),"archive, extraction et données conservées");
        Eq(4000L+500L+StorageCheck.Margin,StorageCheck.Required(1000,4000,500,1000),"archive déjà reçue");
        try { StorageCheck.Ensure(root,2000,_=>1000); throw new Exception("manque d'espace non bloqué"); }
        catch(LauncherException ex) { Eq("E-DISK-001",ex.Error.Code,"espace insuffisant"); }
        try { StorageCheck.Ensure(root,2000,_=>throw new IOException("cannot read disk")); throw new Exception("espace inconnu accepté"); }
        catch(LauncherException ex) { Eq("E-DISK-004",ex.Error.Code,"espace non vérifiable"); }
        var archive=Path.Combine(root,"compressed-budget.zip");
        using(var zip=System.IO.Compression.ZipFile.Open(archive,System.IO.Compression.ZipArchiveMode.Create))
        { using var entry=zip.CreateEntry("large.bin").Open(); entry.Write(new byte[1024*1024]); }
        Eq(1024L*1024,StorageCheck.ExpandedZipBytes(archive),"taille décompressée réelle");
        True(new FileInfo(archive).Length<1024*1024/10,"archive compressée pour tester le sous-calcul");
        StorageCheck.CheckWritable(root);
    }

    private static void LaunchChecks(string root)
    {
        var directory=Path.Combine(root,"launch-preflight");Directory.CreateDirectory(directory);
        try { LaunchPreflight.CheckClient(directory,directory);throw new Exception("installation incomplète acceptée"); }
        catch(LauncherException ex) { Eq("E-GAME-009",ex.Error.Code,"fichier essentiel absent"); }
        foreach(var file in new[]{"H1Z1.exe","mss32.dll","steam_api64.dll","vivoxsdk_x64.dll","ClientConfig.ini"}) File.WriteAllText(Path.Combine(directory,file),"test");
        LaunchPreflight.CheckClient(directory,directory);
        True(!new GameServiceStatus(true,true,true,5,DateTimeOffset.UtcNow).CanPlay,"maintenance bloque lancement");
        True(!new GameServiceStatus(true,false,false,0,DateTimeOffset.UtcNow).CanPlay,"API seule ne prouve pas jeu en ligne");
    }

    // Les 11 fichiers de réglages réellement présents dans le catalogue de production (client 0.23.4.161181).
    private static readonly string[] ProductionPlayerFiles =
    {
        "DeepGraphicsWrapper.ini","Graphics.ini","InputProfile_Default.xml","InputProfile_UIWhiteList.xml","InputProfile_User.xml",
        "LaunchPad.ini","SoundSettings.xml","UI/Settings/container.xml","UI/Settings/reticle.xml","UserOptions.ini","vld.ini",
    };

    private static string Rel(string dir,string relative)=>Path.Combine(dir,relative.Replace('/',Path.DirectorySeparatorChar));

    /// <summary>Installation telle que l'ancien launcher la laisse : marqueur avec fin de ligne, ni inventaire ni cache,
    /// réglages modifiés par le joueur, fichiers à lui (captures, mods), ClientConfig.ini avec un ticket, journaux.</summary>
    private static Dictionary<string,string> WriteOldLauncherInstall(string dir)
    {
        var mine=new Dictionary<string,string>();
        foreach(var f in ProductionPlayerFiles) mine[f]="PLAYER "+f;
        mine["InputProfile_Mine.xml"]="profil personnalisé";
        mine["ClientConfig.ini"]="[Game]\nTicket=lp2.exemple";
        mine["Logs/H1Z1 PlayClient (Live).log"]="journal du jeu";
        mine["screenshots/a.png"]="capture du joueur";
        mine["mods/m.dll"]="mod du joueur";
        foreach(var (relative,content) in mine)
        {
            var path=Rel(dir,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,content);
        }
        Directory.CreateDirectory(Rel(dir,"Assets"));
        File.WriteAllText(Rel(dir,"H1Z1.exe"),"ancien executable");
        File.WriteAllText(Rel(dir,"Assets/a.pack"),"ancien pack");
        File.WriteAllText(Rel(dir,".cotk-client-version"),"0.23.4.161180\r\n");
        return mine;
    }

    private static List<string> Changed(string dir,Dictionary<string,string> expected,Func<string,bool>? only=null)=>
        expected.Where(kv=>only is null||only(kv.Key)).Where(kv=>!File.Exists(Rel(dir,kv.Key))||File.ReadAllText(Rel(dir,kv.Key))!=kv.Value).Select(kv=>kv.Key).ToList();

    private static async Task PlayerSettingsDelta(string root)
    {
        var exe=new byte[3000];new Random(11).NextBytes(exe);
        var pack=new byte[50_000];new Random(12).NextBytes(pack);
        var remote=new Dictionary<string,byte[]>{["H1Z1.exe"]=exe,["Assets/a.pack"]=pack,["UI/Settings/brand-new.xml"]=Encoding.UTF8.GetBytes("nouveau")};
        foreach(var f in ProductionPlayerFiles) remote[f]=Encoding.UTF8.GetBytes("DEFAUT "+f); // le catalogue propose ses propres versions
        var (cdn,dir,release)=Prepare(root,"player-delta",remote);
        using var _=cdn;
        try
        {
            var mine=WriteOldLauncherInstall(dir);
            var result=await new ClientInstaller().EnsureAsync(release,new Sink(),CancellationToken.None);
            True(result is {FullArchive:false,WasUpdate:true},"mise à jour différentielle");
            var changed=Changed(dir,mine);
            True(changed.Count==0,"aucun fichier du joueur modifié ni supprimé après la mise à jour : "+string.Join(", ",changed));
            True(File.ReadAllBytes(Rel(dir,"H1Z1.exe")).SequenceEqual(exe) && File.ReadAllBytes(Rel(dir,"Assets/a.pack")).SequenceEqual(pack),"fichiers du jeu mis à jour");
            Eq("nouveau",File.ReadAllText(Rel(dir,"UI/Settings/brand-new.xml")),"réglage absent du disque installé depuis le catalogue (jamais écrasé s'il existe)");
            Eq("9.9.9.9",ClientInstaller.InstalledVersion(),"nouvelle version marquée");

            // Réparation : elle remet les fichiers du jeu en état mais ne touche jamais aux réglages.
            File.WriteAllText(Rel(dir,"Assets/a.pack"),"corrompu");
            var repaired=await new ClientInstaller().VerifyAsync(release,repair:true,new Sink(),CancellationToken.None);
            True(File.ReadAllBytes(Rel(dir,"Assets/a.pack")).SequenceEqual(pack),"fichier du jeu réparé");
            changed=Changed(dir,mine);
            True(changed.Count==0,"aucun fichier du joueur modifié après réparation : "+string.Join(", ",changed));
            Eq(1,repaired.FilesDownloaded,"seul le fichier corrompu est retéléchargé");
        }
        finally {Environment.SetEnvironmentVariable("COTK_API_URL",null);}
    }

    private static async Task PlayerSettingsFullArchive(string root)
    {
        var exe=Encoding.UTF8.GetBytes("nouvel executable");
        var pack=Encoding.UTF8.GetBytes("nouveau pack");
        var remote=new Dictionary<string,byte[]>{["H1Z1.exe"]=exe,["Assets/a.pack"]=pack};
        foreach(var f in ProductionPlayerFiles) remote[f]=Encoding.UTF8.GetBytes("DEFAUT "+f);
        var (cdn,dir,release)=Prepare(root,"player-full",remote);
        using var _=cdn;
        try
        {
            var mine=WriteOldLauncherInstall(dir);
            cdn.Missing.Add("/client/Assets/a.pack"); // le différentiel échoue : repli sur l'archive complète
            var archive=Zip(new[]{("H1Z1.exe","nouvel executable"),("Assets/a.pack","nouveau pack")}.Concat(ProductionPlayerFiles.Select(f=>(f,"DEFAUT "+f))).ToArray());
            cdn.Add("client.zip",archive);
            release=release with {Sha256=Sha(archive),SizeBytes=archive.Length};
            var installer=new ClientInstaller {ConfirmFullDownload=(_,_)=>Task.FromResult(true)};
            var result=await installer.EnsureAsync(release,new Sink(),CancellationToken.None);
            True(result.FullArchive,"archive complète installée");
            True(File.ReadAllBytes(Rel(dir,"H1Z1.exe")).SequenceEqual(exe),"jeu remplacé par la nouvelle version");
            // Seuls les réglages sont garantis par ce chemin ; les fichiers inconnus (captures, mods) ne le sont pas.
            var changed=Changed(dir,mine,ManifestRules.IsPlayerData);
            True(changed.Count==0,"tous les réglages du joueur restaurés après le remplacement complet : "+string.Join(", ",changed));
        }
        finally {Environment.SetEnvironmentVariable("COTK_API_URL",null);}
    }

    private static void OldLauncherCompatibility(string root)
    {
        // settings.json exactement tel que l'ancien launcher l'écrit (JsonSerializer indenté, seule clé clientDir).
        Directory.CreateDirectory(AppPaths.SettingsDir);
        var customDir=Path.Combine(root,"ancien-client","client");Directory.CreateDirectory(customDir);
        File.WriteAllText(AppPaths.SettingsFile,System.Text.Json.JsonSerializer.Serialize(new {clientDir=customDir},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Settings.Load();
        Eq(customDir,Settings.ClientDirectory,"clientDir de l'ancien launcher lu tel quel");
        Eq(customDir,ClientInstaller.ClientDir,"le Next cible le client choisi avec l'ancien launcher");

        // Marqueur et exécutable : l'ancien écrit « version + fin de ligne ».
        File.WriteAllText(Path.Combine(customDir,"H1Z1.exe"),"exe");
        File.WriteAllText(Path.Combine(customDir,".cotk-client-version"),"0.23.4.161181\r\n");
        var current=new ClientRelease("0.23.4.161181","https://dl.cotk.fr/c.zip",new string('a',64),1);
        var newer=current with {Version="0.23.4.161182"};
        Eq("0.23.4.161181",ClientInstaller.InstalledVersion(),"version installée lue malgré la fin de ligne");
        True(ClientInstaller.IsInstalled(current) && !ClientInstaller.NeedsUpdate(current),"client à jour reconnu, rien à retélécharger");
        True(!ClientInstaller.IsInstalled(newer) && ClientInstaller.NeedsUpdate(newer),"version supérieure : mise à jour proposée (pas une première installation)");

        // Changer un réglage du Next ne perd ni le dossier du client ni les clés inconnues.
        Settings.MinimizeOnPlay=false;
        var saved=File.ReadAllText(AppPaths.SettingsFile);
        True(saved.Contains("clientDir") && saved.Replace("\\\\","\\").Contains(customDir),"clientDir conservé après écriture d'un autre réglage");
        Settings.Load();
        Eq(customDir,Settings.ClientDirectory,"dossier du client inchangé après rechargement");

        // Sans réglage : dossier « client » à côté du launcher, comme l'ancien (README.md à la racine de l'installation).
        File.WriteAllText(AppPaths.SettingsFile,"{}");Settings.Load();
        Eq(Path.Combine(AppPaths.InstallDir,"client"),ClientInstaller.ClientDir,"dossier par défaut identique à celui de l'ancien launcher");
    }

    private static byte[] Zip(params (string name,string content)[] entries)
    {
        using var memory=new MemoryStream();
        using(var zip=new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Create,true))
            foreach(var (name,content) in entries)
                using(var w=new StreamWriter(zip.CreateEntry(name).Open())) w.Write(content);
        return memory.ToArray();
    }

    private static UpdateManifest ManifestWithLauncher(string version,byte[]? package=null,string url="https://dl.cotk.fr/launcher.zip")
    {
        var sha=package is null?new string('a',64):Sha(package);
        return new UpdateManifest(1,
            new LauncherRelease(version,url,sha,package?.Length??1000),
            new ClientRelease("0.23.4.161181","https://dl.cotk.fr/client.zip",new string('b',64),5000));
    }

    private static void LauncherUpdateDownload(string root)
    {
        // Détection : seule une version strictement plus récente déclenche une proposition (la 0.10.10 migre vers 1.x).
        True(LauncherUpdater.IsNewer("1.0.2","0.10.10"),"1.0.2 plus récent que 0.10.10 (comparaison numérique, pas textuelle)");
        True(!LauncherUpdater.IsNewer("0.10.10","0.10.10") && !LauncherUpdater.IsNewer("0.9.9","0.10.10"),"même version ou plus ancienne : rien à faire");
        True(!LauncherUpdater.IsNewer("n'importe quoi","1.0.0"),"version illisible : refusée sans exception");
        True(LauncherUpdater.Check(ManifestWithLauncher("1.0.2"),"1.0.1") is {Version:"1.0.2"},"mise à jour proposée");
        True(LauncherUpdater.Check(ManifestWithLauncher("1.0.1"),"1.0.1") is null,"à jour : aucune proposition");
        True(LauncherUpdater.Check(ManifestWithLauncher("9.9.9",url:""),"1.0.1") is null,"sans paquet publié : aucune proposition");

        var good=Zip((LauncherUpdater.CompatExecutable,"new-exe"));
        using var cdn=new FakeCdn();
        Environment.SetEnvironmentVariable("COTK_API_URL",cdn.BaseUrl);
        try
        {
            var folder=Path.Combine(root,"launcher-dl");Directory.CreateDirectory(folder);
            cdn.Add("launcher.zip",good);
            var release=new LauncherRelease("1.0.2",cdn.BaseUrl+"/launcher.zip",Sha(good),good.Length);
            var path=LauncherUpdater.DownloadAsync(release,null,CancellationToken.None,folder).GetAwaiter().GetResult();
            True(File.Exists(path) && Sha(File.ReadAllBytes(path))==Sha(good),"paquet téléchargé et conforme");
            File.Delete(path);

            void Refused(LauncherRelease wrong,string why)
            {
                try {LauncherUpdater.DownloadAsync(wrong,null,CancellationToken.None,folder).GetAwaiter().GetResult();}
                catch(LauncherException) {True(Directory.GetFiles(folder).Length==0,"aucun fichier laissé après refus : "+why);return;}
                throw new Exception("refus attendu : "+why);
            }
            Refused(release with {Sha256=new string('c',64)},"empreinte fausse");
            Refused(release with {SizeBytes=good.Length-3},"taille publiée inférieure");
            Refused(release with {SizeBytes=good.Length+3},"taille publiée supérieure");
        }
        finally {Environment.SetEnvironmentVariable("COTK_API_URL",null);}
    }

    private static (string target,string archive,string plan) PrepareUpdate(string root,string name,byte[] package,int parentPid=0)
    {
        var target=Path.Combine(root,name,"install");
        Directory.CreateDirectory(Path.Combine(target,"client"));Directory.CreateDirectory(Path.Combine(target,"logs"));Directory.CreateDirectory(Path.Combine(target,"ToggleConsole"));
        File.WriteAllText(Path.Combine(target,"COTK.Launcher.exe"),"old-exe");
        File.WriteAllText(Path.Combine(target,"COTK.Launcher.dll"),"old-dll");
        File.WriteAllText(Path.Combine(target,"old-only.dll"),"old-only");
        File.WriteAllText(Path.Combine(target,"client","H1Z1.exe"),"player-game");
        File.WriteAllText(Path.Combine(target,"logs","launcher.log"),"player-log");
        File.WriteAllText(Path.Combine(target,"ToggleConsole","ToggleConsole.exe"),"staff-console");
        var archive=Path.Combine(root,name,"pkg.zip");File.WriteAllBytes(archive,package);
        var plan=Path.Combine(root,name,"plan.json");
        File.WriteAllText(plan,System.Text.Json.JsonSerializer.Serialize(new LauncherUpdatePlan(archive,target,Path.Combine(target,"COTK.Launcher.exe"),parentPid,Path.Combine(root,name,"updater","COTK.Launcher.exe")),ApiClient.Json));
        return (target,archive,plan);
    }

    private static void LauncherUpdateApply(string root)
    {
        var package=Zip((LauncherUpdater.CompatExecutable,"new-exe"),("COTK.Launcher.dll","new-dll"),("ranks/tier-1.png","png"),("System.Private.CoreLib.dll","new-runtime"));
        var (target,archive,plan)=PrepareUpdate(root,"apply-ok",package,parentPid: 2_000_000_000); // processus inexistant = déjà fermé
        var launched=new List<(string exe,string args)>();
        var code=LauncherUpdater.Apply(plan,(exe,args)=>{launched.Add((exe,args));return true;},TimeSpan.Zero);
        Eq(0,code,"mise à jour appliquée");
        Eq("new-exe",File.ReadAllText(Path.Combine(target,"COTK.Launcher.exe")),"exécutable remplacé");
        Eq("new-dll",File.ReadAllText(Path.Combine(target,"COTK.Launcher.dll")),"bibliothèque remplacée");
        True(File.Exists(Path.Combine(target,"ranks","tier-1.png")) && File.Exists(Path.Combine(target,"System.Private.CoreLib.dll")),"nouveaux fichiers et sous-dossiers copiés");
        Eq("old-only",File.ReadAllText(Path.Combine(target,"old-only.dll")),"fichiers hors paquet conservés");
        Eq("player-game",File.ReadAllText(Path.Combine(target,"client","H1Z1.exe")),"client du joueur intact");
        Eq("player-log",File.ReadAllText(Path.Combine(target,"logs","launcher.log")),"journaux intacts");
        Eq("staff-console",File.ReadAllText(Path.Combine(target,"ToggleConsole","ToggleConsole.exe")),"console staff intacte");
        Eq(1,launched.Count,"un seul relancement");
        Eq(Path.Combine(target,"COTK.Launcher.exe"),launched[0].exe,"relance l'exécutable attendu par les anciens launchers");
        True(launched[0].args.StartsWith("--cleanup-updater \"") && launched[0].args.Contains("updater"),"relance avec --cleanup-updater <copie installatrice>");
        True(!File.Exists(plan) && !File.Exists(archive),"plan et archive temporaires supprimés");
        True(!Directory.GetDirectories(Path.GetDirectoryName(target)!).Any(d=>d.Contains(".update-")||d.Contains(".rollback-")),"aucun dossier d'étape ni de sauvegarde laissé");

        // Un launcher nommé autrement (ex. COTK.Launcher.Next.exe) migre vers le nom commun.
        var renamed=Zip((LauncherUpdater.CompatExecutable,"new-exe"));
        var (target2,_,plan2)=PrepareUpdate(root,"apply-rename",renamed,2_000_000_000);
        File.Move(Path.Combine(target2,"COTK.Launcher.exe"),Path.Combine(target2,"COTK.Launcher.Next.exe"));
        File.WriteAllText(plan2,System.Text.Json.JsonSerializer.Serialize(new LauncherUpdatePlan(Path.Combine(root,"apply-rename","pkg.zip"),target2,Path.Combine(target2,"COTK.Launcher.Next.exe"),2_000_000_000,Path.Combine(root,"apply-rename","updater","x.exe")),ApiClient.Json));
        var launched2=new List<string>();
        Eq(0,LauncherUpdater.Apply(plan2,(exe,_)=>{launched2.Add(exe);return true;},TimeSpan.Zero),"migration depuis un autre nom d'exécutable");
        Eq(Path.Combine(target2,"COTK.Launcher.exe"),launched2[0],"relance sous le nom commun");
    }

    private static void LauncherUpdateRollback(string root)
    {
        // 1) Un fichier verrouillé fait échouer la copie : tout est restauré, l'ancien launcher est relancé.
        var package=Zip((LauncherUpdater.CompatExecutable,"new-exe"),("COTK.Launcher.dll","new-dll"),("zzz-locked.dll","new-locked"),("brand-new.dll","new-file"));
        var (target,archive,plan)=PrepareUpdate(root,"apply-fail",package,2_000_000_000);
        File.WriteAllText(Path.Combine(target,"zzz-locked.dll"),"old-locked");
        var launched=new List<string>();
        int code;
        using(new FileStream(Path.Combine(target,"zzz-locked.dll"),FileMode.Open,FileAccess.Read,FileShare.None))
            code=LauncherUpdater.Apply(plan,(exe,_)=>{launched.Add(exe);return true;},TimeSpan.FromMilliseconds(10));
        Eq(1,code,"échec signalé");
        Eq("old-exe",File.ReadAllText(Path.Combine(target,"COTK.Launcher.exe")),"exécutable d'origine restauré");
        Eq("old-dll",File.ReadAllText(Path.Combine(target,"COTK.Launcher.dll")),"bibliothèque d'origine restaurée");
        Eq("old-locked",File.ReadAllText(Path.Combine(target,"zzz-locked.dll")),"fichier verrouillé intact");
        True(!File.Exists(Path.Combine(target,"brand-new.dll")),"fichier ajouté par la tentative retiré");
        Eq("player-game",File.ReadAllText(Path.Combine(target,"client","H1Z1.exe")),"client du joueur intact");
        Eq(Path.Combine(target,"COTK.Launcher.exe"),launched.Single(),"l'ancien launcher est relancé");
        True(!Directory.GetDirectories(Path.GetDirectoryName(target)!).Any(d=>d.Contains(".update-")||d.Contains(".rollback-")),"aucun dossier d'étape ni de sauvegarde laissé");

        // 2) Paquets refusés avant toute copie.
        void Refused(byte[] bad,string why)
        {
            var (t,_,p)=PrepareUpdate(root,"refuse-"+why.Replace(' ','-'),bad,2_000_000_000);
            var started=false;
            Eq(1,LauncherUpdater.Apply(p,(_,_)=>{started=true;return true;},TimeSpan.Zero),"refusé : "+why);
            Eq("old-exe",File.ReadAllText(Path.Combine(t,"COTK.Launcher.exe")),"rien modifié : "+why);
            Eq("player-game",File.ReadAllText(Path.Combine(t,"client","H1Z1.exe")),"client intact : "+why);
            True(started,"ancien launcher relancé : "+why);
        }
        Refused(Zip(("client/H1Z1.exe","evil"),(LauncherUpdater.CompatExecutable,"x")),"dossier client dans le paquet");
        Refused(Zip(("ToggleConsole/ToggleConsole.exe","evil"),(LauncherUpdater.CompatExecutable,"x")),"console staff dans le paquet");
        Refused(Zip(("logs/launcher.log","evil"),(LauncherUpdater.CompatExecutable,"x")),"journaux dans le paquet");
        Refused(Zip(("autre.exe","x")),"sans COTK.Launcher.exe");
        Refused(Zip(("../evil.dll","x"),(LauncherUpdater.CompatExecutable,"x")),"zip-slip");

        // 3) Plan illisible ou archive absente : aucune action.
        var bad=Path.Combine(root,"bad-plan.json");File.WriteAllText(bad,"{ pas du json");
        Eq(1,LauncherUpdater.Apply(bad,(_,_)=>throw new Exception("ne doit rien lancer"),TimeSpan.Zero),"plan illisible refusé");
    }

    private static void LauncherUpdateCleanup(string root)
    {
        var tempRoot=Path.Combine(root,"fake-temp");Directory.CreateDirectory(tempRoot);
        var ours=Path.Combine(tempRoot,"COTK.Launcher.Updater-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(ours);
        File.WriteAllText(Path.Combine(ours,"COTK.Launcher.exe"),"x");
        var foreign=Path.Combine(tempRoot,"MesDocuments");Directory.CreateDirectory(foreign);File.WriteAllText(Path.Combine(foreign,"precieux.txt"),"x");
        var lookalike=Path.Combine(tempRoot,"COTK.Launcher.Updater-trop-court");Directory.CreateDirectory(lookalike);File.WriteAllText(Path.Combine(lookalike,"a.txt"),"x");
        var outside=Path.Combine(root,"COTK.Launcher.Updater-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"a.txt"),"x");

        LauncherUpdater.Cleanup(Path.Combine(foreign,"COTK.Launcher.exe"),tempRoot,1);
        True(File.Exists(Path.Combine(foreign,"precieux.txt")),"dossier étranger jamais supprimé");
        LauncherUpdater.Cleanup(Path.Combine(lookalike,"x.exe"),tempRoot,1);
        True(Directory.Exists(lookalike),"nom ressemblant mais invalide : conservé");
        LauncherUpdater.Cleanup(Path.Combine(outside,"x.exe"),tempRoot,1);
        True(Directory.Exists(outside),"dossier hors du répertoire temporaire : conservé");
        LauncherUpdater.Cleanup(Path.Combine(ours,"COTK.Launcher.exe"),tempRoot,3);
        True(!Directory.Exists(ours),"notre dossier d'installateur supprimé");
        LauncherUpdater.Cleanup("",tempRoot,1);LauncherUpdater.Cleanup("\0bad",tempRoot,1);
    }

    private static byte[] ConsoleZip(string version,bool withExecutable=true)
    {
        using var memory=new MemoryStream();
        using(var zip=new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Create,true))
        {
            if(withExecutable) using(var w=new StreamWriter(zip.CreateEntry("ToggleConsole.exe").Open())) w.Write("exe-"+version);
            using(var w=new StreamWriter(zip.CreateEntry("data/config.txt").Open())) w.Write("cfg-"+version);
        }
        return memory.ToArray();
    }

    private static async Task StaffConsoleInstall(string root)
    {
        var target=Path.Combine(root,"staff-console","ToggleConsole");
        StaffConsole.TestDirectory=target;
        using var cdn=new FakeCdn();
        Environment.SetEnvironmentVariable("COTK_API_URL",cdn.BaseUrl);
        try
        {
            using var api=new ApiClient();
            var v1=ConsoleZip("1");
            cdn.Add("api/v1/staff/console",Encoding.UTF8.GetBytes($"{{\"sha256\":\"{Sha(v1).ToUpperInvariant()}\",\"sizeBytes\":{v1.Length}}}"));
            cdn.Add("api/v1/staff/console/download",v1);
            var info=await api.GetStaffConsoleInfoAsync("token",CancellationToken.None);
            Eq(Sha(v1),info.Sha256,"empreinte normalisée en minuscules");
            True(StaffConsole.ShouldInstall(StaffConsole.InstalledSha(),info),"installation proposée quand absente");

            await StaffConsole.InstallAsync(api,"token",info,CancellationToken.None);
            True(File.Exists(Path.Combine(target,"ToggleConsole.exe")) && File.Exists(Path.Combine(target,"data","config.txt")),"fichiers extraits, sous-dossier inclus");
            Eq(Sha(v1),StaffConsole.InstalledSha(),"empreinte installée mémorisée");
            True(!StaffConsole.ShouldInstall(StaffConsole.InstalledSha(),info),"rien à refaire quand à jour");

            async Task Refused(StaffConsoleInfo wrong,string why)
            {
                try {await StaffConsole.InstallAsync(api,"token",wrong,CancellationToken.None);}
                catch(LauncherException) {
                    Eq(Sha(v1),StaffConsole.InstalledSha(),"console existante intacte après refus : "+why);
                    Eq("exe-1",File.ReadAllText(Path.Combine(target,"ToggleConsole.exe")),"exécutable existant intact : "+why);
                    return;
                }
                throw new Exception("refus attendu : "+why);
            }

            var v2=ConsoleZip("2");
            cdn.Add("api/v1/staff/console/download",v2);
            await Refused(new StaffConsoleInfo(Sha(v1),v2.Length),"empreinte différente de celle annoncée");
            await Refused(new StaffConsoleInfo(Sha(v2),v2.Length-5),"archive plus grosse qu'annoncé");
            await Refused(new StaffConsoleInfo(Sha(v2),v2.Length+5),"archive incomplète");

            var noExe=ConsoleZip("3",withExecutable:false);
            cdn.Add("api/v1/staff/console/download",noExe);
            await Refused(new StaffConsoleInfo(Sha(noExe),noExe.Length),"archive sans ToggleConsole.exe");

            var update=new StaffConsoleInfo(Sha(v2),v2.Length);
            cdn.Add("api/v1/staff/console/download",v2);
            True(StaffConsole.ShouldInstall(StaffConsole.InstalledSha(),update),"mise à jour proposée quand l'empreinte change");
            await StaffConsole.InstallAsync(api,"token",update,CancellationToken.None);
            Eq("exe-2",File.ReadAllText(Path.Combine(target,"ToggleConsole.exe")),"mise à jour appliquée");
            Eq(Sha(v2),StaffConsole.InstalledSha(),"nouvelle empreinte mémorisée");
        }
        finally {StaffConsole.TestDirectory=null;Environment.SetEnvironmentVariable("COTK_API_URL",null);}
    }

    private static void SignedReleases()
    {
        using var publisher=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key=Convert.ToBase64String(publisher.ExportSubjectPublicKeyInfo());
        var now=DateTimeOffset.UtcNow;
        var sha=new string('a',64);
        UpdateManifest Manifest(bool pinned=true)=>new(1,
            new LauncherRelease("1.0.0","https://dl.cotk.fr/launcher.zip",sha,1000),
            new ClientRelease("0.23.4.161181","https://dl.cotk.fr/client.zip",sha,5000,"H1Z1.exe",true,"https://dl.cotk.fr/files.json",pinned?sha:null,9000));
        SignedPayload Payload(string product="COTK.Launcher.Next",string channel="stable",int days=7,UpdateManifest? m=null)=>
            new(1,product,channel,now.AddMinutes(-1),now.AddDays(days),m??Manifest());

        var good=SignedRelease.Sign(Payload(),publisher);
        var verified=SignedRelease.Verify(good,key,now);
        True(verified.Release.Client.Version=="0.23.4.161181" && verified.Release.Client.ExpandedSizeBytes==9000,"publication valide acceptée");

        void Refused(SignedEnvelope envelope,string why,string? useKey=null)
        {
            try {SignedRelease.Verify(envelope,useKey??key,now);}
            catch(LauncherException ex) when(ex.Error.Code=="E-UPD-004") {return;}
            throw new Exception("publication refusée attendue : "+why);
        }

        var bytes=Convert.FromBase64String(good.Payload);bytes[bytes.Length/2]^=1;
        Refused(good with {Payload=Convert.ToBase64String(bytes)},"contenu altéré");
        Refused(SignedRelease.Sign(Payload(),attacker),"signée par une autre clé");
        Refused(SignedRelease.Sign(Payload(days:-1),publisher),"publication expirée");
        Refused(SignedRelease.Sign(Payload(product:"Autre"),publisher),"mauvais produit");
        Refused(SignedRelease.Sign(Payload(channel:"beta"),publisher),"mauvais canal");
        Refused(SignedRelease.Sign(Payload(m:Manifest(pinned:false)),publisher),"catalogue non épinglé");
        Refused(new SignedEnvelope("pas-du-base64","!!"),"enveloppe illisible");
        Refused(good with {Signature=new string('A',400)},"signature démesurée");
        try {SignedRelease.Verify(good,null,now);throw new Exception("la clé embarquée ne doit pas valider une autre clé");}
        catch(LauncherException) {}
    }

    private static async Task PauseTransfer()
    {
        var control=new TransferPause();control.Pause();
        var pending=control.WaitAsync(CancellationToken.None);
        await Task.Delay(100);True(!pending.IsCompleted,"travail suspendu");
        control.Resume();await pending;True(!control.Paused,"reprise effective");
        using var cancelled=new CancellationTokenSource();control.Pause();
        var wait=control.WaitAsync(cancelled.Token);cancelled.Cancel();
        try {await wait;throw new Exception("pause non annulable");}catch(OperationCanceledException){}
        control.Resume();
    }
}
