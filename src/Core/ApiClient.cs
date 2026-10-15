using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace COTK.Next.Core;

/// <summary>Client unique de l'API COTK. Chaque échec devient une <see cref="LauncherException"/>
/// (code stable + message humain) : plus aucun « Service indisponible » générique.</summary>
internal sealed class ApiClient : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ApiClient()
    {
        var baseUrl = Config.ApiUrl;
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback)))
            throw new LauncherException(Errors.InvalidConfig("L'adresse de l'API doit être en HTTPS."));

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(8),
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
        };
        _http = new HttpClient(handler, disposeHandler: true) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"COTK-Launcher/{Config.Version}");
    }

    // ------------------------------------------------------------------ état du service

    /// <summary>Latence de l'API en ms, ou null si injoignable. Ne lève jamais.</summary>
    public async Task<int?> PingAsync(CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync("healthz", HttpCompletionOption.ResponseHeadersRead, linked.Token);
            return response.IsSuccessStatusCode ? (int)Math.Max(1, watch.ElapsedMilliseconds) : null;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ compte

    public async Task<GameServiceStatus?> GetGameStatusAsync(CancellationToken ct)
    {
        using var response=await SendAsync(HttpMethod.Get,"api/v1/launcher/status",null,null,ct,HttpStatusCode.NotFound);
        if(response.StatusCode==HttpStatusCode.NotFound) return null;
        return await ReadAsync<GameServiceStatus>(response,ct);
    }

    public async Task<Session> LoginAsync(string username, string password, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/launcher/login", new { username, password }, null, ct);
        var token = await ReadAsync<TokenResponse>(response, ct);
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.Username))
            throw new LauncherException(Errors.BadResponse("login: champs manquants"));
        var account = await GetAccountAsync(token.AccessToken, ct);
        return new Session(token.AccessToken, account);
    }

    public async Task<Account> GetAccountAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/launcher/me", null, accessToken, ct);
        var me = await ReadAsync<MeResponse>(response, ct);
        if (string.IsNullOrWhiteSpace(me.Username) || string.IsNullOrWhiteSpace(me.Role))
            throw new LauncherException(Errors.BadResponse("me: champs manquants"));
        return new Account(me.Username, me.Role, me.ExpiresAt);
    }

    public async Task LogoutAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/launcher/logout", null, accessToken, ct);
    }

    // ------------------------------------------------------------------ connexion navigateur (device flow)

    public async Task<DeviceStart> StartDeviceAsync(CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/launcher/device/start", new { }, null, ct);
        var start = await ReadAsync<DeviceStart>(response, ct);
        if (string.IsNullOrWhiteSpace(start.DeviceCode) || string.IsNullOrWhiteSpace(start.UserCode))
            throw new LauncherException(Errors.BadResponse("device/start: champs manquants"));
        if (!Uri.TryCreate(start.VerificationUri, UriKind.Absolute, out var verify)
            || verify.UserInfo.Length != 0
            || !(verify.Scheme == "https" && verify.Host.Equals("cotk.fr", StringComparison.OrdinalIgnoreCase)
                || Config.IsLocalApi && verify.IsLoopback && verify.Scheme is "http" or "https"))
            throw new LauncherException(Errors.UnsafeUpdate("Adresse de validation du compte non autorisée."));
        return start;
    }

    public async Task<DevicePollResult> PollDeviceAsync(string deviceCode, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/launcher/device/token", new { deviceCode }, null, ct,
            HttpStatusCode.Accepted);
        if (response.StatusCode == HttpStatusCode.Accepted)
            return new DevicePollResult(DevicePollStatus.Pending);

        var token = await ReadAsync<TokenResponse>(response, ct);
        return new DevicePollResult(DevicePollStatus.Approved, token.AccessToken, token.Username, token.ExpiresAt);
    }

    // ------------------------------------------------------------------ jeu

    public async Task<GameTicket> CreateGameTicketAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/game-tickets", null, accessToken, ct);
        var ticket = await ReadAsync<TicketResponse>(response, ct);
        if (string.IsNullOrWhiteSpace(ticket.Ticket) || !ticket.Ticket.StartsWith("lp2.", StringComparison.Ordinal))
            throw new LauncherException(Errors.BadResponse("ticket de jeu invalide"));
        return new GameTicket(ticket.Ticket, ticket.ExpiresAt);
    }

    // ------------------------------------------------------------------ contenu public / profil

    /// <summary>Profil de saison du joueur. Null si l'API ne le propose pas encore (404 : backend plus ancien).</summary>
    public async Task<Profile?> GetProfileAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/launcher/profile", null, accessToken, ct, HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var dto = await ReadAsync<ProfileResponse>(response, ct);
        return new Profile(
            dto.Username ?? "",
            dto.Role ?? "user",
            dto.Season ?? "",
            dto.Ranking is null
                ? RankingSummary.Empty
                : dto.Ranking with { Recent = dto.Ranking.Recent ?? new List<RankMatch>() },
            dto.Thresholds ?? RankThresholds.Default);
    }

    public async Task<Leaderboard> GetLeaderboardAsync(int limit, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/leaderboard?limit={limit}", null, null, ct, HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.NotFound) return new Leaderboard(new List<LeaderEntry>(), "");
        var board = await ReadAsync<LeaderboardResponse>(response, ct);
        return new Leaderboard(board.Entries ?? new List<LeaderEntry>(), board.Season ?? "");
    }

    public async Task<List<PatchNote>> GetPatchNotesAsync(int limit, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/v1/patchnotes?limit={limit}", null, null, ct);
        var notes = await ReadAsync<PatchNotesResponse>(response, ct);
        return notes.PatchNotes ?? new List<PatchNote>();
    }

    // ------------------------------------------------------------------ mises à jour / staff

    /// <summary>Production : publication signée ECDSA sur dl.cotk.fr, refusée si la signature, le produit,
    /// le canal ou la validité sont incorrects (pas de repli non signé). API locale (développement, tests) :
    /// manifeste non signé de l'API, jamais accepté hors boucle locale.</summary>
    public async Task<UpdateManifest?> GetManifestAsync(CancellationToken ct)
    {
        if (Config.IsLocalApi)
        {
            using var local = await SendAsync(HttpMethod.Get, "api/v1/launcher/updates", null, null, ct, HttpStatusCode.NotFound);
            if (local.StatusCode == HttpStatusCode.NotFound) return null;
            return await ReadAsync<UpdateManifest>(local, ct);
        }

        using var response = await SendAsync(HttpMethod.Get, SignedRelease.ManifestUrl, null, null, ct, HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.Content.Headers.ContentLength is > 256 * 1024)
            throw new LauncherException(Errors.UnsafeUpdate("La publication signée est trop volumineuse."));
        var envelope = await ReadAsync<SignedEnvelope>(response, ct);
        var signed = SignedRelease.Verify(envelope);
        Log.Info($"publication signée acceptée : client {signed.Release.Client.Version}, publiée {signed.PublishedAt:O}, expire {signed.ExpiresAt:O}");
        return signed.Release;
    }

    public async Task<StaffConsoleInfo> GetStaffConsoleInfoAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/staff/console", null, accessToken, ct);
        var dto = await ReadAsync<StaffConsoleDto>(response, ct);
        if (string.IsNullOrWhiteSpace(dto.Sha256) || dto.SizeBytes <= 0)
            throw new LauncherException(Errors.BadResponse("console staff: métadonnées invalides"));
        return new StaffConsoleInfo(dto.Sha256.ToLowerInvariant(), dto.SizeBytes);
    }

    /// <summary>Télécharge l'archive de la console. Jamais plus que la taille annoncée : un serveur qui en envoie
    /// davantage est refusé avant de remplir le disque (le SHA-256 est vérifié ensuite par l'appelant).</summary>
    public async Task DownloadStaffConsoleAsync(string accessToken, string destination, long expectedSize, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/staff/console/download", null, accessToken, ct);
        if (response.Content.Headers.ContentLength is { } announced && announced != expectedSize)
            throw new LauncherException(Errors.UnsafeUpdate("La taille de la console reçue ne correspond pas à celle annoncée."));
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > expectedSize)
                throw new LauncherException(Errors.UnsafeUpdate("La console reçue est plus grosse qu'annoncé."));
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total != expectedSize)
            throw new LauncherException(Errors.UnsafeUpdate("La console reçue est incomplète."));
    }

    // ------------------------------------------------------------------ plomberie

    public async Task<ReportReceipt> SubmitReportAsync(string token, ReportSubmission report, CancellationToken ct)
    {
        if (!report.Consent || report.Sections.Count == 0) throw new LauncherException(new LauncherError("E-SUPPORT-001", "Extrait non envoyé", "Choisissez un extrait et confirmez son envoi."));
        using var response = await SendAsync(HttpMethod.Post, "api/v1/launcher/reports", report, token, ct, HttpStatusCode.NotFound, HttpStatusCode.RequestEntityTooLarge);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new LauncherException(new LauncherError("E-SUPPORT-002", "Envoi indisponible", "Le service de rapports n'est pas encore disponible. Vous pouvez exporter le diagnostic localement.", Retryable:true));
        if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge) throw new LauncherException(new LauncherError("E-SUPPORT-003", "Extrait trop volumineux", "Choisissez moins de lignes puis réessayez."));
        return await ReadAsync<ReportReceipt>(response, ct);
    }

    public async Task<ReportList> ListReportsAsync(string token, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/staff/launcher-reports", null, token, ct);
        return await ReadAsync<ReportList>(response, ct);
    }

    public async Task<ReportDetails> ReadReportAsync(string token, string id, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/staff/launcher-reports/" + Uri.EscapeDataString(id), null, token, ct);
        return await ReadAsync<ReportDetails>(response, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? bearer,
        CancellationToken ct,
        params HttpStatusCode[] accepted)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            var error = ex is TaskCanceledException or TimeoutException
                ? Errors.Timeout(ex.Message)
                : Errors.FromHttpRequest((HttpRequestException)ex);
            Log.Warn($"{method} {path} -> {error.Code}", ex);
            throw new LauncherException(error, ex);
        }

        if (response.IsSuccessStatusCode || accepted.Contains(response.StatusCode)) return response;

        var status = (int)response.StatusCode;
        string? apiMessage = null;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (text.Length is > 0 and < 4096 && text.TrimStart().StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    apiMessage = e.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException) { }
        response.Dispose();

        var mapped = Errors.FromStatus(status, apiMessage, retryAfter);
        Log.Warn($"{method} {path} -> HTTP {status} ({apiMessage ?? "sans message"}) => {mapped.Code}");
        throw new LauncherException(mapped);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, budget.Token)
                ?? throw new JsonException("Réponse vide.");
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LauncherException(Errors.Timeout("La réponse JSON n'est pas arrivée dans le délai imparti."), ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new LauncherException(Errors.BadResponse(ex.Message), ex);
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record TokenResponse(string AccessToken, string Username, DateTimeOffset ExpiresAt);
    private sealed record MeResponse(string? Id, string Username, string Role, DateTimeOffset ExpiresAt);
    private sealed record TicketResponse(string Ticket, DateTimeOffset ExpiresAt);
    private sealed record PatchNotesResponse(List<PatchNote>? PatchNotes);
    private sealed record LeaderboardResponse(List<LeaderEntry>? Entries, string? Season);
    private sealed record ProfileResponse(string? Username, string? Role, string? Season, RankingSummary? Ranking, RankThresholds? Thresholds);
    private sealed record StaffConsoleDto(string Sha256, long SizeBytes);
}

internal sealed record StaffConsoleInfo(string Sha256, long SizeBytes);
