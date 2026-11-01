using System.Globalization;
using Microsoft.Data.Sqlite;
using SE.Roleplay.Framework.Directory.Models;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>Stores player identity profiles and server-scoped permission grants in shared SQLite.</summary>
public sealed class PlayerProfileStore(DirectoryAuthDatabase database, TimeProvider clock)
{
    public static bool IsValidSteamId64(string? value) =>
        value is { Length: 17 } && value.All(character => character is >= '0' and <= '9');

    public static bool IsValidPermission(string? value) =>
        value is { Length: > 0 and <= 100 } &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or ':' or '.' or '_' or '-');

    public async Task<PlayerProfile?> GetProfileAsync(string steamId64, CancellationToken cancellationToken)
    {
        if (!IsValidSteamId64(steamId64)) return null;
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT steam_id64, display_name, biography, updated_at_utc
            FROM player_profiles WHERE steam_id64 = $steamId;
            """;
        command.Parameters.AddWithValue("$steamId", steamId64);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PlayerProfile(reader.GetString(0), reader.GetString(1), reader.GetString(2), ParseTimestamp(reader.GetString(3)))
            : new PlayerProfile(steamId64, string.Empty, string.Empty, DateTime.UnixEpoch);
    }

    public async Task<PlayerProfile?> UpdateProfileAsync(
        string steamId64, UpdatePlayerProfileRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidSteamId64(steamId64) ||
            string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 40 ||
            request.Biography is null || request.Biography.Length > 500)
            return null;

        var displayName = request.DisplayName.Trim();
        var biography = request.Biography.Trim();
        var updated = FormatTimestamp(clock.GetUtcNow().UtcDateTime);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO player_profiles(steam_id64, display_name, biography, updated_at_utc)
            VALUES($steamId, $displayName, $biography, $updated)
            ON CONFLICT(steam_id64) DO UPDATE SET
                display_name = excluded.display_name,
                biography = excluded.biography,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$steamId", steamId64);
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$biography", biography);
        command.Parameters.AddWithValue("$updated", updated);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new PlayerProfile(steamId64, displayName, biography, ParseTimestamp(updated));
    }

    public async Task<bool> GrantPermissionAsync(
        string serverId, string ownerSteamId64, string playerSteamId64, string permission,
        DateTime? expiresAtUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverId) || !IsValidSteamId64(ownerSteamId64) ||
            !IsValidSteamId64(playerSteamId64) || !IsValidPermission(permission))
            return false;

        var now = clock.GetUtcNow().UtcDateTime;
        if (expiresAtUtc is not null &&
            (expiresAtUtc.Value <= now || expiresAtUtc.Value > now.AddDays(365)))
            return false;

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO player_permission_grants(
                server_id, steam_id64, permission, granted_by_steam_id64, granted_at_utc, expires_at_utc)
            SELECT s.id, $player, $permission, $owner, $now, $expires
            FROM directory_servers s
            WHERE s.id = $server AND s.owner_steam_id64 = $owner
            ON CONFLICT(server_id, steam_id64, permission) DO UPDATE SET
                granted_by_steam_id64 = excluded.granted_by_steam_id64,
                granted_at_utc = excluded.granted_at_utc,
                expires_at_utc = excluded.expires_at_utc;
            """;
        command.Parameters.AddWithValue("$server", serverId);
        command.Parameters.AddWithValue("$owner", ownerSteamId64);
        command.Parameters.AddWithValue("$player", playerSteamId64);
        command.Parameters.AddWithValue("$permission", permission);
        command.Parameters.AddWithValue("$now", FormatTimestamp(now));
        command.Parameters.AddWithValue("$expires", expiresAtUtc is null
            ? DBNull.Value : FormatTimestamp(DateTime.SpecifyKind(expiresAtUtc.Value, DateTimeKind.Utc)));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> RevokePermissionAsync(
        string serverId, string ownerSteamId64, string playerSteamId64, string permission,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverId) || !IsValidSteamId64(ownerSteamId64) ||
            !IsValidSteamId64(playerSteamId64) || !IsValidPermission(permission))
            return false;

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM player_permission_grants
            WHERE server_id = $server AND steam_id64 = $player AND permission = $permission
              AND EXISTS (SELECT 1 FROM directory_servers s
                          WHERE s.id = $server AND s.owner_steam_id64 = $owner);
            """;
        command.Parameters.AddWithValue("$server", serverId);
        command.Parameters.AddWithValue("$owner", ownerSteamId64);
        command.Parameters.AddWithValue("$player", playerSteamId64);
        command.Parameters.AddWithValue("$permission", permission);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<PlayerPermission>> GetPermissionsAsync(
        string serverId, string steamId64, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverId) || !IsValidSteamId64(steamId64))
            return Array.Empty<PlayerPermission>();

        var now = FormatTimestamp(clock.GetUtcNow().UtcDateTime);
        var results = new List<PlayerPermission>();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT server_id, steam_id64, permission, granted_by_steam_id64, granted_at_utc, expires_at_utc
            FROM player_permission_grants
            WHERE server_id = $server AND steam_id64 = $player
              AND (expires_at_utc IS NULL OR expires_at_utc > $now)
            ORDER BY permission;
            """;
        command.Parameters.AddWithValue("$server", serverId);
        command.Parameters.AddWithValue("$player", steamId64);
        command.Parameters.AddWithValue("$now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new PlayerPermission(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                ParseTimestamp(reader.GetString(4)),
                reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5))));
        return results;
    }

    public async Task<bool> HasPermissionAsync(
        string serverId, string steamId64, string permission, CancellationToken cancellationToken)
    {
        if (!IsValidPermission(permission)) return false;
        var permissions = await GetPermissionsAsync(serverId, steamId64, cancellationToken);
        return permissions.Any(grant => string.Equals(grant.Permission, permission, StringComparison.Ordinal));
    }

    private static string FormatTimestamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseTimestamp(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
