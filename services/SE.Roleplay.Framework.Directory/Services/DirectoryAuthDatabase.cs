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
