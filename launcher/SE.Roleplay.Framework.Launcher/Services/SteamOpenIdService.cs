using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SE.Roleplay.Framework.Launcher.Services;

/// <summary>
/// Steam OpenID 2.0 sign-in for a desktop app. Steam credentials are entered only
/// on Steam's own website; this app receives and verifies a SteamID64 assertion.
/// </summary>
public sealed class SteamOpenIdService
{
    private const string SteamEndpoint = "https://steamcommunity.com/openid/login";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string> SignInAsync(CancellationToken cancellationToken = default)
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var realm = $"http://127.0.0.1:{port}/";
        var returnTo = $"{realm}callback?state={state}";
        var query = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnTo,
            ["openid.realm"] = realm,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
        };
        var loginUrl = SteamEndpoint + "?" + string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });

        Dictionary<string, string> assertion;
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(requestLine)) continue;
            string? header;
            do { header = await reader.ReadLineAsync(timeout.Token); } while (!string.IsNullOrEmpty(header));

            var pieces = requestLine.Split(' ');
            if (pieces.Length < 2 || !pieces[1].StartsWith("/callback?", StringComparison.Ordinal))
            {
                await RespondAsync(stream, "404 Not Found", "Not found.");
                continue;
            }

            var callback = ParseQuery(pieces[1].Split('?', 2)[1]);
            if (!callback.TryGetValue("state", out var returnedState) || returnedState.Length != state.Length || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(returnedState)))
            {
                await RespondAsync(stream, "400 Bad Request", "This sign-in request is invalid. You can close this tab.");
                throw new InvalidOperationException("Steam sign-in state validation failed. Please try again.");
            }

            await RespondAsync(stream, "200 OK", "Steam sign-in received. You can close this browser tab and return to the SE1 Launcher.");
            assertion = callback;
            break;
        }

        return await VerifyAssertionAsync(assertion, returnTo, cancellationToken);
    }

    private static async Task<string> VerifyAssertionAsync(Dictionary<string, string> data, string expectedReturnTo, CancellationToken cancellationToken)
    {
        string Get(string key) => data.TryGetValue("openid." + key, out var value) ? value : string.Empty;

        if (Get("mode") != "id_res" ||
            Get("ns") != "http://specs.openid.net/auth/2.0" ||
            Get("return_to") != expectedReturnTo ||
            Get("op_endpoint") is not (SteamEndpoint or "https://steamcommunity.com/openid/"))
            throw new InvalidOperationException("Steam returned an unexpected sign-in response.");

        var claimedId = Get("claimed_id");
        var match = Regex.Match(claimedId, @"^https?://steamcommunity\.com/openid/id/(\d{17})$", RegexOptions.CultureInvariant);
        if (!match.Success || Get("identity") != claimedId)
            throw new InvalidOperationException("Steam did not return a valid SteamID64.");

        var signed = Get("signed").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var required in new[] { "op_endpoint", "claimed_id", "identity", "return_to", "response_nonce" })
            if (!signed.Contains(required, StringComparer.Ordinal))
                throw new InvalidOperationException("Steam's sign-in response is missing a required signature field.");

        var nonce = Get("response_nonce");
        if (nonce.Length < 20 || !DateTimeOffset.TryParseExact(nonce[..20], "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var issuedAt) ||
            issuedAt < DateTimeOffset.UtcNow.AddMinutes(-10) || issuedAt > DateTimeOffset.UtcNow.AddMinutes(2))
            throw new InvalidOperationException("Steam's sign-in response has expired. Please try again.");

        var verification = new Dictionary<string, string>(data, StringComparer.Ordinal)
        {
            ["openid.mode"] = "check_authentication"
        };
        using var content = new FormUrlEncodedContent(verification.Where(p => p.Key.StartsWith("openid.", StringComparison.Ordinal)));
        using var response = await Http.PostAsync(Get("op_endpoint"), content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!Regex.IsMatch(body, @"(^|\r?\n)is_valid:true(\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Steam could not verify the sign-in response.");

        return match.Groups[1].Value;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            if (!result.TryAdd(key, value))
                throw new InvalidOperationException("Steam returned duplicate sign-in parameters.");
        }
        return result;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string message)
    {
        var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>SE1 Launcher</title></head><body style=\"font-family:Segoe UI;background:#101419;color:#f1f5f7;padding:3rem\"><h2>SE1 Roleplay Launcher</h2><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
