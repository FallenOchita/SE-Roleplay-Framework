using System.Security.Cryptography;
using System.Text;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Short-lived, single-use desktop authorization state and PKCE code storage.
/// This in-memory implementation is single-process only; deployments with multiple
/// directory instances must replace it with shared storage providing atomic consumption.
/// </summary>
public sealed class DesktopAuthorizationCodeStore
{
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<string, StateEntry> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CodeEntry> _codes = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public DesktopAuthorizationCodeStore(TimeProvider? clock = null) =>
        _clock = clock ?? TimeProvider.System;

    public string CreateState(string clientId, string redirectUri, string challenge, string browserSession,
        string? clientState = null)
    {
        ValidateInputs(clientId, redirectUri, challenge, browserSession);
        if (clientState is { Length: > 256 } || (clientState is not null &&
            clientState.Any(char.IsControl)))
            throw new ArgumentException("Invalid launcher state.", nameof(clientState));
        var state = NewValue();
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            _states.Add(HashKey(state), new StateEntry(clientId, redirectUri, challenge,
                Hash(browserSession), now.Add(StateLifetime), clientState));
        }
        return state;
    }

    public string? CompleteState(string state, string browserSession, string verifiedSteamId64) =>
        CompleteAuthorization(state, browserSession, verifiedSteamId64)?.Code;

    public DesktopAuthorizationCompletion? CompleteAuthorization(
        string state, string browserSession, string verifiedSteamId64)
    {
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(browserSession) ||
            !IsSteamId(verifiedSteamId64))
            return null;

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            var key = HashKey(state);
            if (!_states.TryGetValue(key, out var entry))
                return null;
            if (entry.ExpiresAt <= now ||
                !CryptographicOperations.FixedTimeEquals(entry.BrowserHash, Hash(browserSession)))
                return null;
            _states.Remove(key);

            var code = NewValue();
            _codes.Add(HashKey(code), new CodeEntry(verifiedSteamId64, entry.ClientId,
                entry.RedirectUri, entry.Challenge, now.Add(CodeLifetime)));
            return new DesktopAuthorizationCompletion(code, entry.RedirectUri, entry.ClientState);
        }
    }

    public bool DenyState(string state, string browserSession) =>
        DenyAuthorization(state, browserSession) is not null;

    public DesktopAuthorizationDenial? DenyAuthorization(string state, string browserSession)
    {
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(browserSession))
            return null;
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            var key = HashKey(state);
            if (!_states.TryGetValue(key, out var entry) || entry.ExpiresAt <= now ||
                !CryptographicOperations.FixedTimeEquals(entry.BrowserHash, Hash(browserSession)))
                return null;
            _states.Remove(key);
            return new DesktopAuthorizationDenial(entry.RedirectUri, entry.ClientState);
        }
    }

    public DesktopAuthorizationGrant? RedeemCode(
        string code, string clientId, string redirectUri, string verifier)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            if (!_codes.Remove(HashKey(code), out var entry))
                return null;
            if (entry.ExpiresAt <= now ||
                !string.Equals(entry.ClientId, clientId, StringComparison.Ordinal) ||
                !string.Equals(entry.RedirectUri, redirectUri, StringComparison.Ordinal) ||
                !IsVerifier(verifier))
                return null;

            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(entry.Challenge), Encoding.ASCII.GetBytes(challenge)))
                return null;

            return new DesktopAuthorizationGrant(entry.SteamId64, entry.ClientId);
        }
    }

    public static bool IsLoopbackRedirect(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttp &&
            uri.Host == "127.0.0.1" &&
            uri.Port > 0 &&
            uri.UserInfo.Length == 0 &&
            uri.Query.Length == 0 &&
            uri.Fragment.Length == 0 &&
            uri.AbsolutePath.StartsWith("/oauth/callback/", StringComparison.Ordinal);
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _states.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            _states.Remove(key);
        foreach (var key in _codes.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            _codes.Remove(key);
    }

    private static void ValidateInputs(string clientId, string redirectUri, string challenge, string browserSession)
    {
        if (clientId != "serpf-windows-launcher")
            throw new ArgumentException("Unrecognized desktop client.", nameof(clientId));
        if (!IsLoopbackRedirect(redirectUri))
            throw new ArgumentException("Only IPv4 loopback callback URLs are supported.", nameof(redirectUri));
        if (challenge.Length != 43 || challenge.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("A PKCE S256 challenge is required.", nameof(challenge));
        if (browserSession.Length is < 32 or > 256)
            throw new ArgumentException("Invalid browser session binding.", nameof(browserSession));
    }

    private static bool IsVerifier(string value) =>
        value.Length is >= 43 and <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

    private static bool IsSteamId(string value) =>
        value.Length == 17 && value.All(char.IsAsciiDigit);

    private static string NewValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static string HashKey(string value) => Convert.ToHexString(Hash(value));
    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record StateEntry(string ClientId, string RedirectUri, string Challenge,
        byte[] BrowserHash, DateTimeOffset ExpiresAt, string? ClientState);
    private sealed record CodeEntry(string SteamId64, string ClientId, string RedirectUri,
        string Challenge, DateTimeOffset ExpiresAt);
}

public sealed record DesktopAuthorizationGrant(string SteamId64, string ClientId);
public sealed record DesktopAuthorizationCompletion(string Code, string RedirectUri, string? ClientState);
public sealed record DesktopAuthorizationDenial(string RedirectUri, string? ClientState);
