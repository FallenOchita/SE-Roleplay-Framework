using System.Globalization;
using Microsoft.Data.Sqlite;
using SE.Roleplay.Framework.Directory.Models;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>Persists server allowlists. Every owner mutation is constrained by the server owner in SQL.</summary>
public sealed class ServerMembershipStore(DirectoryAuthDatabase database, TimeProvider clock)
{
    public async Task<bool> AddMemberAsync(
        string serverId, string ownerSteamId64, string playerSteamId64, string? note,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverId) ||
            !PlayerProfileStore.IsValidSteamId64(ownerSteamId64) ||
            !PlayerProfileStore.IsValidSteamId64(playerSteamId64) ||
            note is { Length: > 200 }) return false;

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO server_members(server_id, steam_id64, added_by_steam_id64, added_at_utc, note)
            SELECT s.id, $player, $owner, $now, $note
            FROM directory_servers s
            WHERE s.id = $server AND s.owner_steam_id64 = $owner
            ON CONFLICT(server_id, steam_id64) DO UPDATE SET
                added_by_steam_id64 = excluded.added_by_steam_id64,
                added_at_utc = excluded.added_at_utc,
                note = excluded.note;
            """;
        command.Parameters.AddWithValue("$server", serverId);
        command.Parameters.AddWithValue("$owner", ownerSteamId64);
        command.Parameters.AddWithValue("$player", playerSteamId64);
        command.Parameters.AddWithValue("$now", Format(clock.GetUtcNow().UtcDateTime));
        command.Parameters.AddWithValue("$note", (note ?? string.Empty).Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> RemoveMemberAsync(
        string serverId, string ownerSteamId64, string playerSteamId64,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverId) ||
            !PlayerProfileStore.IsValidSteamId64(ownerSteamId64) ||
            !PlayerProfileStore.IsValidSteamId64(playerSteamId64)) return false;

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM server_members
            WHERE server_id = $server AND steam_id64 = $player
              AND EXISTS (SELECT 1 FROM directory_servers s
                          WHERE s.id = $server AND s.owner_steam_id64 = $owner);
            """;
        command.Parameters.AddWithValue("$server", serverId);
        command.Parameters.AddWithValue("$owner", ownerSteamId64);
        command.Parameters.AddWithValue("$player", playerSteamId64);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<ServerMember>> GetMembersAsync(
        string serverId, string ownerSteamId64, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverId) || !PlayerProfileStore.IsValidSteamId64(ownerSteamId64))
            return Array.Empty<ServerMember>();

        var members = new List<ServerMember>();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.server_id, m.steam_id64, m.added_by_steam_id64, m.added_at_utc, m.note
            FROM server_members m
            INNER JOIN directory_servers s ON s.id = m.server_id
            WHERE m.server_id = $server AND s.owner_steam_id64 = $owner
            ORDER BY m.added_at_utc, m.steam_id64;
            """;
        command.Parameters.AddWithValue("$server", serverId);
        command.Parameters.AddWithValue("$owner", ownerSteamId64);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            members.Add(new ServerMember(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(4)));
        return members;
    }

    public async Task<ServerMembershipStatus> GetStatusAsync(
        string serverId, string playerSteamId64, CancellationToken cancellationToken)
    {
        var allowed = false;
        if (!string.IsNullOrWhiteSpace(serverId) && PlayerProfileStore.IsValidSteamId64(playerSteamId64))
        {
            await using var connection = database.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS(SELECT 1 FROM server_members m
                    INNER JOIN directory_servers s ON s.id = m.server_id
                    WHERE m.server_id = $server AND m.steam_id64 = $player);
                """;
            command.Parameters.AddWithValue("$server", serverId);
            command.Parameters.AddWithValue("$player", playerSteamId64);
            allowed = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }
        return new ServerMembershipStatus(serverId, playerSteamId64, allowed);
    }

    private static string Format(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
