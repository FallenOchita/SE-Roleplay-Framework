using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Short-lived, single-use desktop authorization state and PKCE code storage.
/// When configured with DirectoryAuthDatabase, records are shared and consumed atomically through SQLite.
/// </summary>
public sealed class DesktopAuthorizationCodeStore
{
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<string, StateEntry> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CodeEntry> _codes = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly DirectoryAuthDatabase? _database;

    public DesktopAuthorizationCodeStore(TimeProvider? clock = null)
        : this(null, clock, useDatabase: false) { }

    public DesktopAuthorizationCodeStore(DirectoryAuthDatabase database, TimeProvider? clock = null)
        : this(database ?? throw new ArgumentNullException(nameof(database)), clock, useDatabase: true) { }

    private DesktopAuthorizationCodeStore(DirectoryAuthDatabase? database, TimeProvider? clock, bool useDatabase = false)
    {
        _database = database;
        _clock = clock ?? TimeProvider.System;
    }

    public string CreateState(string clientId, string redirectUri, string challenge, string browserSession,
        string? clientState = null)
    {
        ValidateInputs(clientId, redirectUri, challenge, browserSession);
        if (clientState is { Length: > 256 } || (clientState is not null && clientState.Any(char.IsControl)))
            throw new ArgumentException("Invalid launcher state.", nameof(clientState));

        var now = _clock.GetUtcNow();
        var browserHash = Hash(browserSession);
        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            Prune(connection, now);
            while (true)
            {
                var state = NewValue();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT OR IGNORE INTO desktop_authorization_states
                        (state_hash, client_id, redirect_uri, code_challenge, browser_session_hash, client_state, expires_at_utc)
                    VALUES ($state, $client, $redirect, $challenge, $browser, $clientState, $expires);
                    """;
                command.Parameters.AddWithValue("$state", HashKey(state));
                command.Parameters.AddWithValue("$client", clientId);
                command.Parameters.AddWithValue("$redirect", redirectUri);
                command.Parameters.AddWithValue("$challenge", challenge);
                command.Parameters.Add("$browser", SqliteType.Blob).Value = browserHash;
                command.Parameters.AddWithValue("$clientState", (object?)clientState ?? DBNull.Value);
                command.Parameters.AddWithValue("$expires", FormatTimestamp(now.Add(StateLifetime)));
                if (command.ExecuteNonQuery() == 1)
                    return state;
            }
        }

        var memoryState = NewValue();
        lock (_gate)
        {
            Prune(now);
            while (!_states.TryAdd(HashKey(memoryState), new StateEntry(clientId, redirectUri, challenge,
                browserHash, now.Add(StateLifetime), clientState)))
                memoryState = NewValue();
        }
        return memoryState;
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
        var stateKey = HashKey(state);
        var browserHash = Hash(browserSession);
        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            StateEntry? entry;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM desktop_authorization_states
                    WHERE state_hash = $state
                      AND browser_session_hash = $browser
                      AND expires_at_utc > $now
                    RETURNING client_id, redirect_uri, code_challenge, client_state;
                    """;
                command.Parameters.AddWithValue("$state", stateKey);
                command.Parameters.Add("$browser", SqliteType.Blob).Value = browserHash;
                command.Parameters.AddWithValue("$now", FormatTimestamp(now));
                using var reader = command.ExecuteReader();
                entry = reader.Read()
                    ? new StateEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        browserHash, now.Add(StateLifetime), reader.IsDBNull(3) ? null : reader.GetString(3))
                    : null;
            }

            if (entry is null)
            {
                transaction.Commit();
                return null;
            }

            var code = NewValue();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO desktop_authorization_codes
                        (code_hash, steam_id64, client_id, redirect_uri, code_challenge, expires_at_utc)
                    VALUES ($code, $steam, $client, $redirect, $challenge, $expires);
                    """;
                command.Parameters.AddWithValue("$code", HashKey(code));
                command.Parameters.AddWithValue("$steam", verifiedSteamId64);
                command.Parameters.AddWithValue("$client", entry.ClientId);
                command.Parameters.AddWithValue("$redirect", entry.RedirectUri);
                command.Parameters.AddWithValue("$challenge", entry.Challenge);
                command.Parameters.AddWithValue("$expires", FormatTimestamp(now.Add(CodeLifetime)));
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            return new DesktopAuthorizationCompletion(code, entry.RedirectUri, entry.ClientState);
        }

        lock (_gate)
        {
            Prune(now);
            if (!_states.TryGetValue(stateKey, out var entry) || entry.ExpiresAt <= now ||
                !CryptographicOperations.FixedTimeEquals(entry.BrowserHash, browserHash))
                return null;
            _states.Remove(stateKey);
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
        var stateKey = HashKey(state);
        var browserHash = Hash(browserSession);

        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM desktop_authorization_states
                WHERE state_hash = $state
                  AND browser_session_hash = $browser
                  AND expires_at_utc > $now
                RETURNING redirect_uri, client_state;
                """;
            command.Parameters.AddWithValue("$state", stateKey);
            command.Parameters.Add("$browser", SqliteType.Blob).Value = browserHash;
            command.Parameters.AddWithValue("$now", FormatTimestamp(now));
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? new DesktopAuthorizationDenial(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1))
                : null;
        }

        lock (_gate)
        {
            Prune(now);
            if (!_states.TryGetValue(stateKey, out var entry) || entry.ExpiresAt <= now ||
                !CryptographicOperations.FixedTimeEquals(entry.BrowserHash, browserHash))
                return null;
            _states.Remove(stateKey);
            return new DesktopAuthorizationDenial(entry.RedirectUri, entry.ClientState);
        }
    }

    public DesktopAuthorizationGrant? RedeemCode(
        string code, string clientId, string redirectUri, string verifier)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var now = _clock.GetUtcNow();
        var codeKey = HashKey(code);

        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            CodeEntry? entry;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM desktop_authorization_codes
                    WHERE code_hash = $code
                    RETURNING steam_id64, client_id, redirect_uri, code_challenge, expires_at_utc;
                    """;
                command.Parameters.AddWithValue("$code", codeKey);
                using var reader = command.ExecuteReader();
                entry = reader.Read()
                    ? new CodeEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture,
                            DateTimeStyles.None))
                    : null;
            }
            transaction.Commit();
            return ValidateCode(entry, clientId, redirectUri, verifier, now);
        }

        lock (_gate)
        {
            Prune(now);
            if (!_codes.Remove(codeKey, out var entry))
                return null;
            return ValidateCode(entry, clientId, redirectUri, verifier, now);
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

    private static DesktopAuthorizationGrant? ValidateCode(
        CodeEntry? entry, string clientId, string redirectUri, string verifier, DateTimeOffset now)
    {
        if (entry is null || entry.ExpiresAt <= now ||
            !string.Equals(entry.ClientId, clientId, StringComparison.Ordinal) ||
            !string.Equals(entry.RedirectUri, redirectUri, StringComparison.Ordinal) ||
            !IsVerifier(verifier))
            return null;

        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(entry.Challenge), Encoding.ASCII.GetBytes(challenge))
            ? new DesktopAuthorizationGrant(entry.SteamId64, entry.ClientId)
            : null;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _states.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            _states.Remove(key);
        foreach (var key in _codes.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            _codes.Remove(key);
    }

    private static void Prune(SqliteConnection connection, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM desktop_authorization_states WHERE expires_at_utc <= $now;
            DELETE FROM desktop_authorization_codes WHERE expires_at_utc <= $now;
            """;
        command.Parameters.AddWithValue("$now", FormatTimestamp(now));
        command.ExecuteNonQuery();
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

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
        value is not null && value.Length is >= 43 and <= 128 &&
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
