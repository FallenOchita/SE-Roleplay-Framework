using System.Security.Cryptography;
using System.Text;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Process-local opaque launcher token storage. Access and refresh credentials are
/// stored only as SHA-256 hashes. Replace with shared transactional storage before
/// running more than one directory instance.
/// </summary>
public sealed class DesktopTokenStore
{
    public const string RegisteredClientId = "serpf-windows-launcher";
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, AccessEntry> _accessTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RefreshEntry> _refreshTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FamilyEntry> _families = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public DesktopTokenStore(TimeProvider? clock = null) =>
        _clock = clock ?? TimeProvider.System;

    public DesktopTokenPair? Issue(string steamId64, string clientId)
    {
        if (!IsSteamId(steamId64) || clientId != RegisteredClientId)
            return null;

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            var familyId = NewToken(24);
            _families.Add(familyId, new FamilyEntry());
            return CreatePair(familyId, steamId64, clientId, now);
        }
    }

    public DesktopTokenPair? RotateRefreshToken(string refreshToken, string clientId)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || clientId != RegisteredClientId)
            return null;

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            var key = HashKey(refreshToken);
            if (!_refreshTokens.TryGetValue(key, out var entry))
                return null;

            if (entry.Consumed)
            {
                RevokeFamily(entry.FamilyId);
                return null;
            }

            if (entry.ExpiresAt <= now ||
                !string.Equals(entry.ClientId, clientId, StringComparison.Ordinal) ||
                !_families.TryGetValue(entry.FamilyId, out var family) || family.Revoked)
                return null;

            // Keep the consumed hash until its original expiry so replay can revoke
            // every access/refresh credential issued to this token family.
            _refreshTokens[key] = entry with { Consumed = true };
            return CreatePair(entry.FamilyId, entry.SteamId64, entry.ClientId, now);
        }
    }

    public DesktopTokenIdentity? ValidateAccessToken(string accessToken, string clientId)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || clientId != RegisteredClientId)
            return null;

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            if (!_accessTokens.TryGetValue(HashKey(accessToken), out var entry) ||
                entry.ExpiresAt <= now ||
                !string.Equals(entry.ClientId, clientId, StringComparison.Ordinal) ||
                !_families.TryGetValue(entry.FamilyId, out var family) ||
                family.Revoked)
                return null;

            return new DesktopTokenIdentity(entry.SteamId64, entry.ClientId);
        }
    }

    private DesktopTokenPair CreatePair(string familyId, string steamId64, string clientId, DateTimeOffset now)
    {
        var accessToken = NewToken(32);
        var refreshToken = NewToken(48);
        _accessTokens.Add(HashKey(accessToken), new AccessEntry(
            familyId, steamId64, clientId, now.Add(AccessTokenLifetime)));
        _refreshTokens.Add(HashKey(refreshToken), new RefreshEntry(
            familyId, steamId64, clientId, now.Add(RefreshTokenLifetime), Consumed: false));

        return new DesktopTokenPair(
            accessToken,
            refreshToken,
            "Bearer",
            (int)AccessTokenLifetime.TotalSeconds);
    }

    private void RevokeFamily(string familyId)
    {
        if (_families.TryGetValue(familyId, out var family))
            _families[familyId] = family with { Revoked = true };
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _accessTokens.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            _accessTokens.Remove(key);
        foreach (var key in _refreshTokens.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            _refreshTokens.Remove(key);

        var activeFamilies = _accessTokens.Values.Select(x => x.FamilyId)
            .Concat(_refreshTokens.Values.Select(x => x.FamilyId))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var familyId in _families.Keys.Where(x => !activeFamilies.Contains(x)).ToArray())
            _families.Remove(familyId);
    }

    private static bool IsSteamId(string value) =>
        value.Length == 17 && value.All(char.IsAsciiDigit);

    private static string NewToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string HashKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record AccessEntry(
        string FamilyId, string SteamId64, string ClientId, DateTimeOffset ExpiresAt);
    private sealed record RefreshEntry(
        string FamilyId, string SteamId64, string ClientId, DateTimeOffset ExpiresAt, bool Consumed);
    private sealed record FamilyEntry(bool Revoked = false);
}

public sealed record DesktopTokenPair(
    string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

public sealed record DesktopTokenIdentity(string SteamId64, string ClientId);
