using Microsoft.Extensions.Configuration;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class DirectoryAuthDatabaseTests
{
    [Fact]
    public async Task Initialize_CreatesVersionFiveMembershipSchema()
    {
        var path = CreateDatabasePath();
        try
        {
            var database = CreateDatabase(path);
            await database.InitializeAsync();
            Assert.Equal(5, await database.GetSchemaVersionAsync());
            var tables = await database.GetTableNamesAsync();
            Assert.Contains("steam_login_states", tables);
            Assert.Contains("steam_openid_nonces", tables);
            Assert.Contains("directory_servers", tables);
            Assert.Contains("directory_server_imports", tables);
            Assert.Contains("player_profiles", tables);
            Assert.Contains("player_permission_grants", tables);
            Assert.Contains("server_members", tables);
            Assert.Contains("desktop_authorization_states", tables);
            Assert.Contains("desktop_authorization_codes", tables);
            Assert.Contains("desktop_token_families", tables);
            Assert.Contains("desktop_access_tokens", tables);
            Assert.Contains("desktop_refresh_tokens", tables);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public async Task Initialize_IsIdempotentAcrossDatabaseInstances()
    {
        var path = CreateDatabasePath();
        try
        {
            var first = CreateDatabase(path);
            var second = CreateDatabase(path);
            await first.InitializeAsync();
            await second.InitializeAsync();
            Assert.Equal(5, await first.GetSchemaVersionAsync());
            Assert.Equal(5, await second.GetSchemaVersionAsync());
        }
        finally { DeleteDatabase(path); }
    }

    private static DirectoryAuthDatabase CreateDatabase(string path)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Directory:AuthDatabasePath"] = path
            })
            .Build();
        return new DirectoryAuthDatabase(configuration);
    }

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), "serpf-auth-tests", Guid.NewGuid().ToString("N"), "auth.db");

    private static void DeleteDatabase(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (System.IO.Directory.Exists(directory))
            System.IO.Directory.Delete(directory, recursive: true);
    }
}
