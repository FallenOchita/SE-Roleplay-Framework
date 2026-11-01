using Microsoft.Data.Sqlite;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Initializes the shared SQLite database used by directory authentication stores.
/// Authentication state, authorization codes, and launcher tokens use these tables for
/// cross-instance persistence and atomic replay/rotation protection.
/// </summary>
public sealed class DirectoryAuthDatabase
{
    private readonly string _connectionString;

    public DirectoryAuthDatabase(IConfiguration configuration)
    {
        var path = configuration["Directory:AuthDatabasePath"]
            ?? Environment.GetEnvironmentVariable("SERPF_DIRECTORY_AUTH_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "data", "directory-auth.db");

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            System.IO.Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = 10
        }.ToString();
    }

    /// <summary>Creates a new connection to the configured shared authentication database.</summary>
    public SqliteConnection CreateConnection() => new(_connectionString);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = FULL;
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 10000;
                """;
            await pragmas.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS steam_login_states (
                state_hash TEXT PRIMARY KEY,
                browser_session_hash BLOB NOT NULL,
                return_path TEXT NULL,
                expires_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_steam_login_states_expiry
                ON steam_login_states(expires_at_utc);

            CREATE TABLE IF NOT EXISTS steam_openid_nonces (
                nonce_hash TEXT PRIMARY KEY,
                expires_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_steam_openid_nonces_expiry
                ON steam_openid_nonces(expires_at_utc);

            CREATE TABLE IF NOT EXISTS desktop_authorization_states (
                state_hash TEXT PRIMARY KEY,
                client_id TEXT NOT NULL,
                redirect_uri TEXT NOT NULL,
                code_challenge TEXT NOT NULL,
                browser_session_hash BLOB NOT NULL,
                client_state TEXT NULL,
                expires_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_desktop_authorization_states_expiry
                ON desktop_authorization_states(expires_at_utc);

            CREATE TABLE IF NOT EXISTS desktop_authorization_codes (
                code_hash TEXT PRIMARY KEY,
                steam_id64 TEXT NOT NULL,
                client_id TEXT NOT NULL,
                redirect_uri TEXT NOT NULL,
                code_challenge TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_desktop_authorization_codes_expiry
                ON desktop_authorization_codes(expires_at_utc);

            CREATE TABLE IF NOT EXISTS desktop_token_families (
                family_id TEXT PRIMARY KEY,
                revoked INTEGER NOT NULL DEFAULT 0 CHECK (revoked IN (0, 1)),
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS desktop_access_tokens (
                token_hash TEXT PRIMARY KEY,
                family_id TEXT NOT NULL REFERENCES desktop_token_families(family_id) ON DELETE CASCADE,
                steam_id64 TEXT NOT NULL,
                client_id TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_desktop_access_tokens_expiry
                ON desktop_access_tokens(expires_at_utc);
            CREATE INDEX IF NOT EXISTS ix_desktop_access_tokens_family
                ON desktop_access_tokens(family_id);

            CREATE TABLE IF NOT EXISTS desktop_refresh_tokens (
                token_hash TEXT PRIMARY KEY,
                family_id TEXT NOT NULL REFERENCES desktop_token_families(family_id) ON DELETE CASCADE,
                steam_id64 TEXT NOT NULL,
                client_id TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                consumed INTEGER NOT NULL DEFAULT 0 CHECK (consumed IN (0, 1))
            );
            CREATE INDEX IF NOT EXISTS ix_desktop_refresh_tokens_expiry
                ON desktop_refresh_tokens(expires_at_utc);
            CREATE INDEX IF NOT EXISTS ix_desktop_refresh_tokens_family
                ON desktop_refresh_tokens(family_id);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
                VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
                VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

            CREATE TABLE IF NOT EXISTS directory_servers (
                id TEXT PRIMARY KEY,
                owner_steam_id64 TEXT NULL,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                address TEXT NOT NULL,
                port INTEGER NOT NULL,
                players INTEGER NOT NULL,
                max_players INTEGER NOT NULL,
                region TEXT NOT NULL,
                status TEXT NOT NULL,
                version TEXT NOT NULL,
                website_url TEXT NOT NULL,
                discord_url TEXT NOT NULL,
                rules_url TEXT NOT NULL,
                tags_json TEXT NOT NULL,
                api_key_hash TEXT NULL,
                last_heartbeat_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_directory_servers_owner
                ON directory_servers(owner_steam_id64);
            CREATE INDEX IF NOT EXISTS ix_directory_servers_name
                ON directory_servers(name COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS directory_server_imports (
                source_path TEXT PRIMARY KEY,
                imported_at_utc TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
                VALUES (3, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

            CREATE TABLE IF NOT EXISTS player_profiles (
                steam_id64 TEXT PRIMARY KEY CHECK(length(steam_id64) = 17),
                display_name TEXT NOT NULL,
                biography TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS player_permission_grants (
                server_id TEXT NOT NULL REFERENCES directory_servers(id) ON DELETE CASCADE,
                steam_id64 TEXT NOT NULL CHECK(length(steam_id64) = 17),
                permission TEXT NOT NULL,
                granted_by_steam_id64 TEXT NOT NULL CHECK(length(granted_by_steam_id64) = 17),
                granted_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NULL,
                PRIMARY KEY(server_id, steam_id64, permission)
            );
            CREATE INDEX IF NOT EXISTS ix_player_permission_grants_player
                ON player_permission_grants(steam_id64, server_id);
            CREATE INDEX IF NOT EXISTS ix_player_permission_grants_expiry
                ON player_permission_grants(expires_at_utc);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
                VALUES (4, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

            CREATE TABLE IF NOT EXISTS server_members (
                server_id TEXT NOT NULL REFERENCES directory_servers(id) ON DELETE CASCADE,
                steam_id64 TEXT NOT NULL CHECK(length(steam_id64) = 17),
                added_by_steam_id64 TEXT NOT NULL CHECK(length(added_by_steam_id64) = 17),
                added_at_utc TEXT NOT NULL,
                note TEXT NOT NULL DEFAULT '',
                PRIMARY KEY(server_id, steam_id64)
            );
            CREATE INDEX IF NOT EXISTS ix_server_members_player
                ON server_members(steam_id64, server_id);

            INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
                VALUES (5, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlySet<string>> GetTableNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            names.Add(reader.GetString(0));
        return names;
    }
}
