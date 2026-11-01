using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Opaque launcher token storage. Credentials are stored only as SHA-256 hashes.
/// The SQLite-backed constructor provides cross-instance atomic refresh rotation and family revocation.
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
    private readonly DirectoryAuthDatabase? _database;

    public DesktopTokenStore(TimeProvider? clock = null)
        : this(null, clock, useDatabase: false) { }

    public DesktopTokenStore(DirectoryAuthDatabase database, TimeProvider? clock = null)
        : this(database ?? throw new ArgumentNullException(nameof(database)), clock, useDatabase: true) { }

    private DesktopTokenStore(DirectoryAuthDatabase? database, TimeProvider? clock, bool useDatabase)
    {
        _database = database;
        _clock = clock ?? TimeProvider.System;
    }

    public DesktopTokenPair? Issue(string steamId64, string clientId)
    {
        if (!IsSteamId(steamId64) || clientId != RegisteredClientId)
            return null;

        var now = _clock.GetUtcNow();
        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            Prune(connection, transaction, now);
            var familyId = NewToken(24);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO desktop_token_families (family_id, revoked, created_at_utc)
                    VALUES ($family, 0, $created);
                    """;
                command.Parameters.AddWithValue("$family", familyId);
                command.Parameters.AddWithValue("$created", FormatTimestamp(now));
                command.ExecuteNonQuery();
            }
            var pair = CreatePair(connection, transaction, familyId, steamId64, clientId, now);
            transaction.Commit();
            return pair;
        }

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
        var key = HashKey(refreshToken);
        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            Prune(connection, transaction, now);

            string? familyId;
            string? steamId64;
            string? storedClientId;
            bool consumed;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT family_id, steam_id64, client_id, consumed
                    FROM desktop_refresh_tokens
                    WHERE token_hash = $token;
                    """;
                command.Parameters.AddWithValue("$token", key);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    transaction.Commit();
                    return null;
                }
                familyId = reader.GetString(0);
                steamId64 = reader.GetString(1);
                storedClientId = reader.GetString(2);
                consumed = reader.GetInt32(3) != 0;
            }

            if (consumed)
            {
                RevokeFamily(connection, transaction, familyId!);
                transaction.Commit();
                return null;
            }

            if (!string.Equals(storedClientId, clientId, StringComparison.Ordinal) ||
                !FamilyIsActive(connection, transaction, familyId!))
            {
                transaction.Commit();
                return null;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE desktop_refresh_tokens
                    SET consumed = 1
                    WHERE token_hash = $token AND consumed = 0;
                    """;
                command.Parameters.AddWithValue("$token", key);
                if (command.ExecuteNonQuery() != 1)
                {
                    // A competing replay/rotation must never produce a second token pair.
                    RevokeFamily(connection, transaction, familyId!);
                    transaction.Commit();
                    return null;
                }
            }

            var rotated = CreatePair(connection, transaction, familyId!, steamId64!, storedClientId!, now);
            transaction.Commit();
            return rotated;
        }

        lock (_gate)
        {
            Prune(now);
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

            _refreshTokens[key] = entry with { Consumed = true };
            return CreatePair(entry.FamilyId, entry.SteamId64, entry.ClientId, now);
        }
    }

    public DesktopTokenIdentity? ValidateAccessToken(string accessToken, string clientId)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || clientId != RegisteredClientId)
            return null;

        var now = _clock.GetUtcNow();
        var key = HashKey(accessToken);
        if (_database is not null)
        {
            using var connection = _database.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT access.steam_id64, access.client_id
                FROM desktop_access_tokens AS access
                INNER JOIN desktop_token_families AS family
                    ON family.family_id = access.family_id
                WHERE access.token_hash = $token
                  AND access.client_id = $client
                  AND access.expires_at_utc > $now
                  AND family.revoked = 0;
                """;
            command.Parameters.AddWithValue("$token", key);
            command.Parameters.AddWithValue("$client", clientId);
            command.Parameters.AddWithValue("$now", FormatTimestamp(now));
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? new DesktopTokenIdentity(reader.GetString(0), reader.GetString(1))
                : null;
        }

        lock (_gate)
        {
            Prune(now);
            if (!_accessTokens.TryGetValue(key, out var entry) ||
                entry.ExpiresAt <= now ||
                !string.Equals(entry.ClientId, clientId, StringComparison.Ordinal) ||
                !_families.TryGetValue(entry.FamilyId, out var family) ||
                family.Revoked)
                return null;

            return new DesktopTokenIdentity(entry.SteamId64, entry.ClientId);
        }
    }

    private DesktopTokenPair CreatePair(
        SqliteConnection connection, SqliteTransaction transaction,
        string familyId, string steamId64, string clientId, DateTimeOffset now)
    {
        var accessToken = NewToken(32);
        var refreshToken = NewToken(48);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO desktop_access_tokens
                    (token_hash, family_id, steam_id64, client_id, expires_at_utc)
                VALUES ($access, $family, $steam, $client, $accessExpiry);
                INSERT INTO desktop_refresh_tokens
                    (token_hash, family_id, steam_id64, client_id, expires_at_utc, consumed)
                VALUES ($refresh, $family, $steam, $client, $refreshExpiry, 0);
                """;
            command.Parameters.AddWithValue("$access", HashKey(accessToken));
            command.Parameters.AddWithValue("$refresh", HashKey(refreshToken));
            command.Parameters.AddWithValue("$family", familyId);
            command.Parameters.AddWithValue("$steam", steamId64);
            command.Parameters.AddWithValue("$client", clientId);
            command.Parameters.AddWithValue("$accessExpiry", FormatTimestamp(now.Add(AccessTokenLifetime)));
            command.Parameters.AddWithValue("$refreshExpiry", FormatTimestamp(now.Add(RefreshTokenLifetime)));
            command.ExecuteNonQuery();
        }

        return new DesktopTokenPair(accessToken, refreshToken, "Bearer", (int)AccessTokenLifetime.TotalSeconds);
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

    private static bool FamilyIsActive(SqliteConnection connection, SqliteTransaction transaction, string familyId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM desktop_token_families
                WHERE family_id = $family AND revoked = 0
            );
            """;
        command.Parameters.AddWithValue("$family", familyId);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static void RevokeFamily(SqliteConnection connection, SqliteTransaction transaction, string familyId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE desktop_token_families SET revoked = 1 WHERE family_id = $family;
            """;
        command.Parameters.AddWithValue("$family", familyId);
        command.ExecuteNonQuery();
    }

    private static void Prune(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM desktop_access_tokens WHERE expires_at_utc <= $now;
            DELETE FROM desktop_refresh_tokens WHERE expires_at_utc <= $now;
            DELETE FROM desktop_token_families
            WHERE NOT EXISTS (
                SELECT 1 FROM desktop_access_tokens WHERE family_id = desktop_token_families.family_id
            ) AND NOT EXISTS (
                SELECT 1 FROM desktop_refresh_tokens WHERE family_id = desktop_token_families.family_id
            );
            """;
        command.Parameters.AddWithValue("$now", FormatTimestamp(now));
        command.ExecuteNonQuery();
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

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

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
