using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SE.Roleplay.Framework.Directory.Models;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Transactional SQLite-backed server directory. A configured legacy JSON file is imported
/// once per database/source path during initialization; all subsequent reads and writes use SQLite.
/// </summary>
public sealed class ServerDirectoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DirectoryAuthDatabase _database;
    private readonly string _legacyFilePath;

    public ServerDirectoryStore(IConfiguration configuration, DirectoryAuthDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _legacyFilePath = Path.GetFullPath(
            configuration["Directory:DataPath"]
            ?? Environment.GetEnvironmentVariable("SERPF_DIRECTORY_DATA_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "data", "servers.json"));
    }

    /// <summary>Imports an existing JSON directory exactly once, without overwriting SQLite records.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        List<DirectoryServer> legacyServers = new();
        if (File.Exists(_legacyFilePath))
        {
            await using var stream = File.OpenRead(_legacyFilePath);
            var document = await JsonSerializer.DeserializeAsync<DirectoryDocument>(stream, JsonOptions, cancellationToken);
            legacyServers = document?.Servers ?? new List<DirectoryServer>();
        }

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM directory_server_imports WHERE source_path = $path LIMIT 1;";
            check.Parameters.AddWithValue("$path", _legacyFilePath);
            if (await check.ExecuteScalarAsync(cancellationToken) is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }

        foreach (var server in legacyServers)
            await InsertServerAsync(connection, transaction, server, ignoreDuplicate: true, cancellationToken);

        await using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = """
                INSERT INTO directory_server_imports (source_path, imported_at_utc)
                VALUES ($path, $now);
                """;
            mark.Parameters.AddWithValue("$path", _legacyFilePath);
            mark.Parameters.AddWithValue("$now", FormatTimestamp(DateTime.UtcNow));
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DirectoryServer>> GetPublicServersAsync(CancellationToken cancellationToken)
    {
        var servers = await ReadServersAsync(
            "SELECT * FROM directory_servers ORDER BY name COLLATE NOCASE, id;",
            null, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var server in servers)
            server.Status = now - server.LastHeartbeatUtc <= TimeSpan.FromSeconds(90) ? "Online" : "Offline";
        return servers.Select(SanitizeForDisplay).ToList();
    }

    public Task<(DirectoryServer Server, string ApiKey)> RegisterAsync(
        RegisterServerRequest request, CancellationToken cancellationToken) =>
        RegisterCoreAsync(request, ownerSteamId64: null, cancellationToken);

    public async Task<IReadOnlyList<DirectoryServer>> GetOwnedServersAsync(
        string ownerSteamId64, CancellationToken cancellationToken)
    {
        if (!IsValidSteamId64(ownerSteamId64))
            throw new ArgumentException("A verified SteamID64 is required.", nameof(ownerSteamId64));

        var servers = await ReadServersAsync(
            "SELECT * FROM directory_servers WHERE owner_steam_id64 = $owner ORDER BY name COLLATE NOCASE, id;",
            command => command.Parameters.AddWithValue("$owner", ownerSteamId64),
            cancellationToken);
        return servers.Select(SanitizeForDisplay).ToList();
    }

    public async Task<(DirectoryServer Server, string ApiKey)> RegisterOwnedAsync(
        RegisterServerRequest request, string ownerSteamId64, CancellationToken cancellationToken)
    {
        if (!IsValidSteamId64(ownerSteamId64))
            throw new ArgumentException("A verified SteamID64 is required.", nameof(ownerSteamId64));
        return await RegisterCoreAsync(request, ownerSteamId64, cancellationToken);
    }

    public async Task<bool> UpdateOwnedAsync(
        string id, string ownerSteamId64, RegisterServerRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidSteamId64(ownerSteamId64)) return false;
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = UpdateSql + " WHERE id = $id AND owner_steam_id64 = $owner;";
        AddRegistrationParameters(command, request);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$owner", ownerSteamId64);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DeleteOwnedAsync(
        string id, string ownerSteamId64, CancellationToken cancellationToken)
    {
        if (!IsValidSteamId64(ownerSteamId64)) return false;
        return await ExecuteDeleteAsync(
            "DELETE FROM directory_servers WHERE id = $id AND owner_steam_id64 = $owner;",
            command =>
            {
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$owner", ownerSteamId64);
            }, cancellationToken);
    }

    public async Task<string?> RotateOwnedApiKeyAsync(
        string id, string ownerSteamId64, CancellationToken cancellationToken)
    {
        if (!IsValidSteamId64(ownerSteamId64)) return null;
        var apiKey = CreateApiKey();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE directory_servers SET api_key_hash = $hash
            WHERE id = $id AND owner_steam_id64 = $owner;
            """;
        command.Parameters.AddWithValue("$hash", HashKey(apiKey));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$owner", ownerSteamId64);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? apiKey : null;
    }

    public async Task<bool> HeartbeatAsync(
        string id, string apiKey, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        DirectoryServer? server;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT * FROM directory_servers WHERE id = $id LIMIT 1;";
            select.Parameters.AddWithValue("$id", id);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            server = await reader.ReadAsync(cancellationToken) ? ReadServer(reader) : null;
        }

        if (server is null || !VerifyKey(apiKey, server.ApiKeyHash))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var maxPlayers = request.MaxPlayers > 0 ? Math.Min(request.MaxPlayers, 10000) : server.MaxPlayers;
        var players = Math.Clamp(request.Players, 0, maxPlayers);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE directory_servers
            SET max_players = $max, players = $players, version = $version, last_heartbeat_utc = $heartbeat
            WHERE id = $id AND api_key_hash = $hash;
            """;
        update.Parameters.AddWithValue("$max", maxPlayers);
        update.Parameters.AddWithValue("$players", players);
        update.Parameters.AddWithValue("$version", request.Version?.Trim() ?? string.Empty);
        update.Parameters.AddWithValue("$heartbeat", FormatTimestamp(DateTime.UtcNow));
        update.Parameters.AddWithValue("$id", id);
        update.Parameters.AddWithValue("$hash", server.ApiKeyHash!);
        var changed = await update.ExecuteNonQueryAsync(cancellationToken) == 1;
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) =>
        ExecuteDeleteAsync(
            "DELETE FROM directory_servers WHERE id = $id;",
            command => command.Parameters.AddWithValue("$id", id),
            cancellationToken);

    private async Task<(DirectoryServer Server, string ApiKey)> RegisterCoreAsync(
        RegisterServerRequest request, string? ownerSteamId64, CancellationToken cancellationToken)
    {
        var apiKey = CreateApiKey();
        var server = new DirectoryServer
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerSteamId64 = ownerSteamId64,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Address = request.Address.Trim(),
            Port = request.Port,
            MaxPlayers = request.MaxPlayers,
            Region = request.Region?.Trim() ?? string.Empty,
            WebsiteUrl = request.WebsiteUrl?.Trim() ?? string.Empty,
            DiscordUrl = request.DiscordUrl?.Trim() ?? string.Empty,
            RulesUrl = request.RulesUrl?.Trim() ?? string.Empty,
            Tags = NormalizeTags(request.Tags),
            ApiKeyHash = HashKey(apiKey),
            LastHeartbeatUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
        };

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        await InsertServerAsync(connection, null, server, ignoreDuplicate: false, cancellationToken);
        return (server, apiKey);
    }

    private async Task<bool> ExecuteDeleteAsync(
        string sql, Action<SqliteCommand> configure, CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<List<DirectoryServer>> ReadServersAsync(
        string sql, Action<SqliteCommand>? configure, CancellationToken cancellationToken)
    {
        var servers = new List<DirectoryServer>();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            servers.Add(ReadServer(reader));
        return servers;
    }

    private static async Task InsertServerAsync(
        SqliteConnection connection, SqliteTransaction? transaction, DirectoryServer server,
        bool ignoreDuplicate, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT {(ignoreDuplicate ? "OR IGNORE " : string.Empty)}INTO directory_servers (
                id, owner_steam_id64, name, description, address, port, players, max_players,
                region, status, version, website_url, discord_url, rules_url, tags_json,
                api_key_hash, last_heartbeat_utc)
            VALUES (
                $id, $owner, $name, $description, $address, $port, $players, $maxPlayers,
                $region, $status, $version, $website, $discord, $rules, $tags, $apiKeyHash, $heartbeat);
            """;
        command.Parameters.AddWithValue("$id", server.Id);
        command.Parameters.AddWithValue("$owner", (object?)server.OwnerSteamId64 ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", server.Name);
        command.Parameters.AddWithValue("$description", server.Description);
        command.Parameters.AddWithValue("$address", server.Address);
        command.Parameters.AddWithValue("$port", server.Port);
        command.Parameters.AddWithValue("$players", server.Players);
        command.Parameters.AddWithValue("$maxPlayers", server.MaxPlayers);
        command.Parameters.AddWithValue("$region", server.Region);
        command.Parameters.AddWithValue("$status", server.Status);
        command.Parameters.AddWithValue("$version", server.Version);
        command.Parameters.AddWithValue("$website", server.WebsiteUrl);
        command.Parameters.AddWithValue("$discord", server.DiscordUrl);
        command.Parameters.AddWithValue("$rules", server.RulesUrl);
        command.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(server.Tags, JsonOptions));
        command.Parameters.AddWithValue("$apiKeyHash", (object?)server.ApiKeyHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$heartbeat", FormatTimestamp(server.LastHeartbeatUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DirectoryServer ReadServer(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        OwnerSteamId64 = reader.IsDBNull(reader.GetOrdinal("owner_steam_id64")) ? null : reader.GetString(reader.GetOrdinal("owner_steam_id64")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        Description = reader.GetString(reader.GetOrdinal("description")),
        Address = reader.GetString(reader.GetOrdinal("address")),
        Port = reader.GetInt32(reader.GetOrdinal("port")),
        Players = reader.GetInt32(reader.GetOrdinal("players")),
        MaxPlayers = reader.GetInt32(reader.GetOrdinal("max_players")),
        Region = reader.GetString(reader.GetOrdinal("region")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        Version = reader.GetString(reader.GetOrdinal("version")),
        WebsiteUrl = reader.GetString(reader.GetOrdinal("website_url")),
        DiscordUrl = reader.GetString(reader.GetOrdinal("discord_url")),
        RulesUrl = reader.GetString(reader.GetOrdinal("rules_url")),
        Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("tags_json")), JsonOptions) ?? new(),
        ApiKeyHash = reader.IsDBNull(reader.GetOrdinal("api_key_hash")) ? null : reader.GetString(reader.GetOrdinal("api_key_hash")),
        LastHeartbeatUtc = ParseTimestamp(reader.GetString(reader.GetOrdinal("last_heartbeat_utc")))
    };

    private static DirectoryServer SanitizeForDisplay(DirectoryServer server) => new()
    {
        Id = server.Id,
        Name = server.Name,
        Description = server.Description,
        Address = server.Address,
        Port = server.Port,
        Players = server.Players,
        MaxPlayers = server.MaxPlayers,
        Region = server.Region,
        Status = server.Status,
        Version = server.Version,
        WebsiteUrl = server.WebsiteUrl,
        DiscordUrl = server.DiscordUrl,
        RulesUrl = server.RulesUrl,
        Tags = server.Tags.ToList(),
        LastHeartbeatUtc = server.LastHeartbeatUtc,
        ApiKeyHash = null,
        OwnerSteamId64 = null
    };

    private static readonly string UpdateSql = """
        UPDATE directory_servers SET
            name = $name, description = $description, address = $address, port = $port,
            max_players = $maxPlayers, region = $region, website_url = $website,
            discord_url = $discord, rules_url = $rules, tags_json = $tags
        """;

    private static void AddRegistrationParameters(SqliteCommand command, RegisterServerRequest request)
    {
        command.Parameters.AddWithValue("$name", request.Name.Trim());
        command.Parameters.AddWithValue("$description", request.Description?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$address", request.Address.Trim());
        command.Parameters.AddWithValue("$port", request.Port);
        command.Parameters.AddWithValue("$maxPlayers", request.MaxPlayers);
        command.Parameters.AddWithValue("$region", request.Region?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$website", request.WebsiteUrl?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$discord", request.DiscordUrl?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$rules", request.RulesUrl?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(NormalizeTags(request.Tags), JsonOptions));
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags) =>
        (tags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    private static string CreateApiKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsValidSteamId64(string? value) =>
        value is { Length: 17 } && value.All(character => character is >= '0' and <= '9');

    public static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static bool VerifyKey(string provided, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(expectedHash)) return false;
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        byte[] expected;
        try { expected = Convert.FromHexString(expectedHash); }
        catch (FormatException) { return false; }
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string FormatTimestamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseTimestamp(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
