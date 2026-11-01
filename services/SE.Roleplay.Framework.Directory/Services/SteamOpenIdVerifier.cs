using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Verifies Steam OpenID 2.0 assertions with Steam itself. A successful result is the
/// SteamID64 extracted from Steam's canonical claimed identifier; caller-supplied IDs
/// are never accepted as identity proof.
/// </summary>
public sealed class SteamOpenIdVerifier
{
    public const string SteamOpenIdEndpoint = "https://steamcommunity.com/openid/login";
    private static readonly string[] RequiredSignedFields =
    {
        "op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle"
    };

    private static readonly Regex ClaimedIdPattern = new(
        @"^https://steamcommunity\.com/openid/id/(?<steamId>[0-9]{17})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumedNonces = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public SteamOpenIdVerifier(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the verified SteamID64, or null when the assertion is invalid.
    /// expectedReturnTo must be the exact HTTPS callback URL, including the one-time state.
    /// </summary>
    public async Task<string?> VerifyAsync(
        IReadOnlyDictionary<string, string> assertion,
        string expectedReturnTo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        if (!Uri.TryCreate(expectedReturnTo, UriKind.Absolute, out var returnUri) ||
            returnUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(returnUri.AbsoluteUri, expectedReturnTo, StringComparison.Ordinal))
            return null;

        if (!TryGet(assertion, "openid.ns", out var openIdNamespace) ||
            openIdNamespace != "http://specs.openid.net/auth/2.0" ||
            !TryGet(assertion, "openid.mode", out var mode) || mode != "id_res" ||
            !TryGet(assertion, "openid.op_endpoint", out var endpoint) ||
            !string.Equals(endpoint, SteamOpenIdEndpoint, StringComparison.Ordinal) ||
            !TryGet(assertion, "openid.return_to", out var returnedTo) ||
            !string.Equals(returnedTo, expectedReturnTo, StringComparison.Ordinal) ||
            !TryGet(assertion, "openid.claimed_id", out var claimedId) ||
            !TryGet(assertion, "openid.identity", out var identity) ||
            !string.Equals(claimedId, identity, StringComparison.Ordinal) ||
            !TryGet(assertion, "openid.response_nonce", out var nonce) ||
            !IsFreshNonce(nonce) ||
            !TryGet(assertion, "openid.signed", out var signed) ||
            !TryGet(assertion, "openid.sig", out _) ||
            !TryGet(assertion, "openid.assoc_handle", out _))
            return null;

        var match = ClaimedIdPattern.Match(claimedId);
        if (!match.Success)
            return null;

        var signedFields = signed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (RequiredSignedFields.Any(required => !signedFields.Contains(required, StringComparer.Ordinal)))
            return null;

        var form = assertion
            .Where(pair => pair.Key.StartsWith("openid.", StringComparison.Ordinal) &&
                           pair.Key != "openid.mode")
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value))
            .Append(new KeyValuePair<string, string>("openid.mode", "check_authentication"));

        using var request = new HttpRequestMessage(HttpMethod.Post, SteamOpenIdEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.ParseAdd("text/plain");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var finalRequestUri = response.RequestMessage?.RequestUri;
        if (response.StatusCode != HttpStatusCode.OK ||
            (finalRequestUri is not null &&
             !string.Equals(finalRequestUri.AbsoluteUri, SteamOpenIdEndpoint, StringComparison.Ordinal)))
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var isValid = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => string.Equals(line.Trim(), "is_valid:true", StringComparison.OrdinalIgnoreCase));
        if (!isValid)
            return null;

        var now = _timeProvider.GetUtcNow();
        PruneConsumedNonces(now);
        if (!_consumedNonces.TryAdd(nonce, now))
            return null;

        return match.Groups["steamId"].Value;
    }

    private bool IsFreshNonce(string nonce)
    {
        // Steam's nonce starts with an ISO UTC timestamp followed by a unique suffix.
        if (nonce.Length < 20 ||
            !DateTimeOffset.TryParseExact(
                nonce[..20],
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            return false;

        var age = _timeProvider.GetUtcNow() - timestamp;
        return age >= TimeSpan.FromMinutes(-1) && age <= TimeSpan.FromMinutes(5);
    }

    private void PruneConsumedNonces(DateTimeOffset now)
    {
        foreach (var entry in _consumedNonces)
        {
            if (now - entry.Value > TimeSpan.FromMinutes(10))
                _consumedNonces.TryRemove(entry.Key, out _);
        }
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> values, string key, out string value)
    {
        if (values.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
