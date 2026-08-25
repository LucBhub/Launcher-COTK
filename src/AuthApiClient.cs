using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace COTK.Launcher;

internal sealed record LauncherAccount(string Username, string Role, DateTimeOffset ExpiresAt)
{
    public bool IsAdmin => string.Equals(Role, "admin", StringComparison.Ordinal);
}

internal sealed record LauncherSession(string AccessToken, LauncherAccount Account);
internal sealed record GameTicket(string Value, DateTimeOffset ExpiresAt);

internal sealed class AuthApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public AuthApiException(HttpStatusCode? statusCode, Exception? inner = null)
        : base("The authentication service request failed.", inner) => StatusCode = statusCode;
}

internal sealed class AuthApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public AuthApiClient()
    {
        var configured = Environment.GetEnvironmentVariable("COTK_API_URL");
        var baseUrl = string.IsNullOrWhiteSpace(configured) ? "http://localhost:8080" : configured.Trim();
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("COTK_API_URL must be an absolute HTTP or HTTPS URL.");
        if (baseUri.Scheme == Uri.UriSchemeHttp && !baseUri.IsLoopback)
            throw new InvalidOperationException("COTK_API_URL must use HTTPS when it is not local.");

        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<LauncherSession> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/launcher/login", new { username, password }, null, cancellationToken);
        var result = await ReadJsonAsync<TokenResponse>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.AccessToken)
            || string.IsNullOrWhiteSpace(result.Username)
            || result.ExpiresAt == default)
            throw new AuthApiException(response.StatusCode);

        var account = await GetCurrentAccountAsync(result.AccessToken, cancellationToken);
        return new LauncherSession(result.AccessToken, account);
    }

    public async Task<LauncherAccount> GetCurrentAccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/launcher/me", null, accessToken, cancellationToken);
        var result = await ReadJsonAsync<AccountResponse>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.Username) || string.IsNullOrWhiteSpace(result.Role) || result.ExpiresAt == default)
            throw new AuthApiException(response.StatusCode);
        return new LauncherAccount(result.Username, result.Role, result.ExpiresAt);
    }

    public async Task LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/launcher/logout", null, accessToken, cancellationToken);
    }

    public async Task<GameTicket> CreateGameTicketAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/game-tickets", null, accessToken, cancellationToken);
        var result = await ReadJsonAsync<TicketResponse>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.Ticket) || !result.Ticket.StartsWith("lp2.", StringComparison.Ordinal))
            throw new AuthApiException(response.StatusCode);
        return new GameTicket(result.Ticket, result.ExpiresAt);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? bearer,
        CancellationToken cancellationToken,
        params HttpStatusCode[] additionalSuccessCodes)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AuthApiException(null, ex);
        }

        if (!response.IsSuccessStatusCode && !additionalSuccessCodes.Contains(response.StatusCode))
        {
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new AuthApiException(statusCode);
        }
        return response;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Empty response.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new AuthApiException(response.StatusCode, ex);
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record TokenResponse(string AccessToken, string Username, DateTimeOffset ExpiresAt);
    private sealed record AccountResponse(string Id, string Username, string Role, DateTimeOffset ExpiresAt);
    private sealed record TicketResponse(string Ticket, DateTimeOffset ExpiresAt);
}
