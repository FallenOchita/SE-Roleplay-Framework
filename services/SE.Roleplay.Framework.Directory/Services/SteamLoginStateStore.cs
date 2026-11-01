using System.Security.Cryptography;
using System.Text;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Stores short-lived, single-use Steam login states bound to a server-generated browser session.
/// The browser session identifier must come from a secure HttpOnly cookie, never from request input.
/// </summary>
public sealed class SteamLoginStateStore
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;

    public SteamLoginStateStore(TimeProvider? timeProvider = null, TimeSpan? lifetime = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifetime = lifetime ?? DefaultLifetime;
        if (_lifetime <= TimeSpan.Zero || _lifetime > DefaultLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Login state lifetime must be greater than zero and no more than five minutes.");
    }

    /// <summary>Creates a cryptographically random state bound to a server-generated browser session ID.</summary>
    public string Create(string browserSessionId)
    {
        ValidateBrowserSessionId(browserSessionId);
        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var stateHash = Hash(state);
        var sessionHash = Hash(browserSessionId);
        var entry = new Entry(sessionHash, _timeProvider.GetUtcNow().Add(_lifetime));

        lock (_gate)
        {
            PruneExpired(_timeProvider.GetUtcNow());
            // A collision is cryptographically negligible, but never overwrite an existing entry.
            while (!_entries.TryAdd(stateHash, entry))
            {
                state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                stateHash = Hash(state);
            }
        }

        return state;
    }

    /// <summary>
    /// Atomically consumes a state only when it is unexpired and bound to the same browser session.
    /// Invalid or mismatched attempts do not consume another browser's valid state.
    /// </summary>
    public bool TryConsume(string state, string browserSessionId)
    {
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(browserSessionId))
            return false;

        var stateHash = Hash(state);
        var sessionHash = Hash(browserSessionId);
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            PruneExpired(now);
            if (!_entries.TryGetValue(stateHash, out var entry))
                return false;

            if (entry.ExpiresAt <= now ||
                !CryptographicOperations.FixedTimeEquals(entry.BrowserSessionHash, sessionHash))
                return false;

            return _entries.Remove(stateHash);
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
                _entries.Remove(pair.Key);
        }
    }

    private static void ValidateBrowserSessionId(string browserSessionId)
    {
        if (string.IsNullOrWhiteSpace(browserSessionId) ||
            browserSessionId.Length is < 32 or > 256)
            throw new ArgumentException("A server-generated browser session ID between 32 and 256 characters is required.", nameof(browserSessionId));
    }

    private static byte[] Hash(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record Entry(byte[] BrowserSessionHash, DateTimeOffset ExpiresAt);
}
