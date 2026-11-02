using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

namespace SE.Roleplay.Framework.Launcher.Services;

/// <summary>
/// OAuth authorization-code + PKCE client for the directory's registered Windows launcher.
/// The browser performs Steam authentication; the launcher receives only a short-lived code.
/// </summary>
public sealed class DesktopAuthorizationService
{
    public const string ClientId = "serpf-windows-launcher";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private string? _accessToken;

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(_accessToken);
    public string? AccessToken => _accessToken;

    public async Task<DesktopTokenPair> AuthorizeAsync(string directoryUrl, string expectedSteamId64, CancellationToken cancellationToken = default)
    {
        var directory = ValidateDirectoryUrl(directoryUrl);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var state = CreateRandomValue(32);
        var verifier = CreateRandomValue(48);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var redirectUri = $"http://127.0.0.1:{port}/oauth/callback/{state}";
        var authorize = new Uri(directory, "/oauth/authorize");
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state
        };
        var authorizeUrl = authorize.AbsoluteUri + "?" + string.Join("&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        var code = await ReceiveAuthorizationCodeAsync(listener, state, timeout.Token);
        using var response = await Http.PostAsJsonAsync(new Uri(directory, "/oauth/token"),
            new DesktopTokenRequest("authorization_code", ClientId, code, redirectUri, verifier, null),
            timeout.Token);
        response.EnsureSuccessStatusCode();
        var pair = await response.Content.ReadFromJsonAsync<DesktopTokenPair>(cancellationToken: timeout.Token)
            ?? throw new InvalidOperationException("The directory returned an empty token response.");
        ValidateTokenPair(pair);
        _accessToken = pair.AccessToken;
        await VerifySteamIdentityAsync(directory, expectedSteamId64, cancellationToken);
        return pair;
    }

    public async Task<DesktopTokenPair> RefreshAsync(string directoryUrl, string refreshToken, string expectedSteamId64,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("A refresh token is required.", nameof(refreshToken));
        var directory = ValidateDirectoryUrl(directoryUrl);
        using var response = await Http.PostAsJsonAsync(new Uri(directory, "/oauth/token"),
            new DesktopTokenRequest("refresh_token", ClientId, null, null, null, refreshToken),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var pair = await response.Content.ReadFromJsonAsync<DesktopTokenPair>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The directory returned an empty token response.");
        ValidateTokenPair(pair);
        _accessToken = pair.AccessToken;
        await VerifySteamIdentityAsync(directory, expectedSteamId64, cancellationToken);
        return pair;
    }

    private async Task VerifySteamIdentityAsync(Uri directory, string expectedSteamId64, CancellationToken cancellationToken)
    {
        if (expectedSteamId64.Length != 17 || !expectedSteamId64.All(char.IsAsciiDigit))
            throw new InvalidOperationException("The saved Steam identity is invalid.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(directory, "/api/launcher/me"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var identity = await response.Content.ReadFromJsonAsync<DesktopIdentity>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The directory returned no authenticated identity.");
        if (!string.Equals(identity.SteamId64, expectedSteamId64, StringComparison.Ordinal))
        {
            _accessToken = null;
            throw new InvalidOperationException("The browser authorized a different Steam account. Sign in with the same Steam account in both windows.");
        }
    }

    public void ClearAccessToken() => _accessToken = null;

    private static async Task<string> ReceiveAuthorizationCodeAsync(
        TcpListener listener, string expectedState, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(requestLine)) continue;
            string? header;
            do { header = await reader.ReadLineAsync(cancellationToken); }
            while (!string.IsNullOrEmpty(header));

            var parts = requestLine.Split(' ');
            if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.Ordinal))
            {
                await RespondAsync(stream, "400 Bad Request", "Invalid authorization callback.");
                continue;
            }

            var callbackUri = new Uri("http://127.0.0.1" + parts[1]);
            if (!callbackUri.AbsolutePath.Equals($"/oauth/callback/{expectedState}", StringComparison.Ordinal))
            {
                await RespondAsync(stream, "404 Not Found", "This callback does not belong to the active sign-in.");
                continue;
            }

            var query = ParseQuery(callbackUri.Query);
            if (!query.TryGetValue("state", out var returnedState) ||
                !FixedTimeEquals(expectedState, returnedState))
            {
                await RespondAsync(stream, "400 Bad Request", "The sign-in state did not match. Return to the launcher.");
                throw new InvalidOperationException("Directory sign-in state validation failed.");
            }
            if (query.TryGetValue("error", out var error))
            {
                await RespondAsync(stream, "200 OK", "Authorization was not approved. You may close this tab.");
                throw new OperationCanceledException(error == "access_denied"
                    ? "Directory authorization was denied." : "Directory authorization failed.");
            }
            if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code) || code.Length > 512)
            {
                await RespondAsync(stream, "400 Bad Request", "The directory did not return an authorization code.");
                throw new InvalidOperationException("Directory sign-in returned no authorization code.");
            }

            await RespondAsync(stream, "200 OK", "Directory sign-in complete. You can close this tab and return to the SE1 Launcher.");
            return code;
        }
    }

    private static Uri ValidateDirectoryUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Set the directory to a valid HTTPS base URL in Launcher Settings.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/') + "/");
    }

    private static void ValidateTokenPair(DesktopTokenPair pair)
    {
        if (string.IsNullOrWhiteSpace(pair.AccessToken) || string.IsNullOrWhiteSpace(pair.RefreshToken) ||
            pair.TokenType != "Bearer" || pair.ExpiresIn is < 30 or > 3600)
            throw new InvalidOperationException("The directory returned an invalid launcher token response.");
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
            if (!values.TryAdd(key, value))
                throw new InvalidOperationException("The directory returned duplicate callback parameters.");
        }
        return values;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string message)
    {
        var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>SE1 Launcher</title></head><body><h2>SE1 Roleplay Launcher</h2><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private static string CreateRandomValue(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record DesktopTokenRequest(
    string GrantType, string ClientId, string? Code, string? RedirectUri,
    string? CodeVerifier, string? RefreshToken);

public sealed record DesktopTokenPair(
    string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

public sealed record DesktopIdentity(string SteamId64, string ClientId);
