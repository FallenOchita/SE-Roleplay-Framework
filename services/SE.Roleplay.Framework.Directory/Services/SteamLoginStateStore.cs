using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Stores short-lived, single-use Steam login states bound to a server-generated browser session.
/// When configured with DirectoryAuthDatabase, state is persisted and consumed atomically in SQLite.
/// The browser session identifier must come from a secure HttpOnly cookie, never from request input.
/// </summary>
public sealed class SteamLoginStateStore
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly DirectoryAuthDatabase? _database;

    public SteamLoginStateStore(TimeProvider? timeProvider = null, TimeSpan? lifetime = null)
        : this(null, timeProvider, lifetime) { }

    public SteamLoginStateStore(DirectoryAuthDatabase database, TimeProvider? timeProvider = null,
        TimeSpan? lifetime = null)
        : this((DirectoryAuthDatabase?)database, timeProvider, lifetime) { }

    private SteamLoginStateStore(DirectoryAuthDatabase? database, TimeProvider? timeProvider, TimeSpan? lifetime)
    {
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifetime = lifetime ?? DefaultLifetime;
        if (_lifetime <= TimeSpan.Zero || _lifetime > DefaultLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Login state lifetime must be greater than zero and no more than five minutes.");
    }

    /// <summary>Creates a cryptographically random state bound to a server-generated browser session ID.</summary>
    public string Create(string browserSessionId, string? returnPath = null)
    {
        ValidateBrowserSessionId(browserSessionId);
        if (returnPath is not null && !IsSafeLocalReturnPath(returnPath))
            throw new ArgumentException("Return path must be a safe local path.", nameof(returnPath));

        var now = _timeProvider.GetUtcNow();
        var sessionHash = Hash(browserSessionId);
        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            PruneExpired(connection, now);
            while (true)
            {
                var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT OR IGNORE INTO steam_login_states
                        (state_hash, browser_session_hash, return_path, expires_at_utc)
                    VALUES ($state, $session, $returnPath, $expires);
                    """;
                command.Parameters.AddWithValue("$state", Convert.ToHexString(Hash(state)));
                command.Parameters.Add("$session", SqliteType.Blob).Value = sessionHash;
                command.Parameters.AddWithValue("$returnPath", (object?)returnPath ?? DBNull.Value);
                command.Parameters.AddWithValue("$expires", FormatTimestamp(now.Add(_lifetime)));
                if (command.ExecuteNonQuery() == 1)
                    return state;
            }
        }

        var memoryState = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var stateHash = Convert.ToHexString(Hash(memoryState));
        var entry = new Entry(sessionHash, now.Add(_lifetime), returnPath);
        lock (_gate)
        {
            PruneExpired(now);
            while (!_entries.TryAdd(stateHash, entry))
            {
                memoryState = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                stateHash = Convert.ToHexString(Hash(memoryState));
            }
        }
        return memoryState;
    }

    /// <summary>
    /// Atomically consumes a state only when it is unexpired and bound to the same browser session.
    /// Invalid or mismatched attempts do not consume another browser's valid state.
    /// </summary>
    public bool TryConsume(string state, string browserSessionId) =>
        TryConsume(state, browserSessionId, out _);

    public bool TryConsume(string state, string browserSessionId, out string? returnPath)
    {
        returnPath = null;
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(browserSessionId))
            return false;

        var stateHash = Convert.ToHexString(Hash(state));
        var sessionHash = Hash(browserSessionId);
        var now = _timeProvider.GetUtcNow();

        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM steam_login_states
                WHERE state_hash = $state
                  AND browser_session_hash = $session
                  AND expires_at_utc > $now
                RETURNING return_path;
                """;
            command.Parameters.AddWithValue("$state", stateHash);
            command.Parameters.Add("$session", SqliteType.Blob).Value = sessionHash;
            command.Parameters.AddWithValue("$now", FormatTimestamp(now));
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return false;
            returnPath = reader.IsDBNull(0) ? null : reader.GetString(0);
            return true;
        }

        lock (_gate)
        {
            PruneExpired(now);
            if (!_entries.TryGetValue(stateHash, out var entry))
                return false;
            if (entry.ExpiresAt <= now ||
                !CryptographicOperations.FixedTimeEquals(entry.BrowserSessionHash, sessionHash))
                return false;
            _entries.Remove(stateHash);
            returnPath = entry.ReturnPath;
            return true;
        }
    }

    private static bool IsSafeLocalReturnPath(string value) =>
        value.StartsWith("/oauth/authorize?", StringComparison.Ordinal) &&
        !value.StartsWith("//", StringComparison.Ordinal) &&
        !value.Contains('\\') &&
        !value.Any(char.IsControl) &&
        Uri.TryCreate("https://directory.invalid" + value, UriKind.Absolute, out var uri) &&
        uri.Host == "directory.invalid" &&
        uri.AbsolutePath == "/oauth/authorize";

    private void PruneExpired(DateTimeOffset now)
    {
        var expiredKeys = _entries
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expiredKeys)
            _entries.Remove(key);
    }

    private static void PruneExpired(SqliteConnection connection, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM steam_login_states WHERE expires_at_utc <= $now;";
        command.Parameters.AddWithValue("$now", FormatTimestamp(now));
        command.ExecuteNonQuery();
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

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

    private sealed record Entry(byte[] BrowserSessionHash, DateTimeOffset ExpiresAt, string? ReturnPath);
}
