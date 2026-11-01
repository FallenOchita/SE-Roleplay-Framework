using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SE.Roleplay.Framework.Directory.Models;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class ServerDirectoryOwnerStoreTests
{
    private const string OwnerA = "76561198012345678";
    private const string OwnerB = "76561198087654321";

    [Fact]
    public async Task RegisterOwned_AssignsOwnerAndDoesNotExposeSecrets()
    {
        using var store = await TestStore.CreateAsync();
        var (server, apiKey) = await store.Store.RegisterOwnedAsync(CreateRequest("Owned A"), OwnerA, CancellationToken.None);

        Assert.Equal(OwnerA, server.OwnerSteamId64);
        Assert.NotEqual(apiKey, server.ApiKeyHash);
        Assert.Single(await store.Store.GetOwnedServersAsync(OwnerA, CancellationToken.None));
        Assert.Empty(await store.Store.GetOwnedServersAsync(OwnerB, CancellationToken.None));

        var ownerView = Assert.Single(await store.Store.GetOwnedServersAsync(OwnerA, CancellationToken.None));
        Assert.Null(ownerView.OwnerSteamId64);
        Assert.Null(ownerView.ApiKeyHash);
    }

    [Fact]
    public async Task OwnerCannotUpdateDeleteOrRotateAnotherOwnersListing()
    {
        using var store = await TestStore.CreateAsync();
        var (server, originalKey) = await store.Store.RegisterOwnedAsync(CreateRequest("Owner B server"), OwnerB, CancellationToken.None);

        Assert.False(await store.Store.UpdateOwnedAsync(server.Id, OwnerA, CreateRequest("Hijacked"), CancellationToken.None));
        Assert.False(await store.Store.DeleteOwnedAsync(server.Id, OwnerA, CancellationToken.None));
        Assert.Null(await store.Store.RotateOwnedApiKeyAsync(server.Id, OwnerA, CancellationToken.None));

        Assert.Equal("Owner B server", Assert.Single(await store.Store.GetOwnedServersAsync(OwnerB, CancellationToken.None)).Name);
        Assert.True(await store.Store.HeartbeatAsync(server.Id, originalKey,
            new HeartbeatRequest { Players = 1, MaxPlayers = 10, Version = "test" }, CancellationToken.None));
    }

    [Fact]
    public async Task OwnerCanUpdateDeleteAndRotateOwnListing()
    {
        using var store = await TestStore.CreateAsync();
        var (server, originalKey) = await store.Store.RegisterOwnedAsync(CreateRequest("Before"), OwnerA, CancellationToken.None);

        Assert.True(await store.Store.UpdateOwnedAsync(server.Id, OwnerA, CreateRequest("After"), CancellationToken.None));
        Assert.Equal("After", Assert.Single(await store.Store.GetOwnedServersAsync(OwnerA, CancellationToken.None)).Name);

        var rotatedKey = await store.Store.RotateOwnedApiKeyAsync(server.Id, OwnerA, CancellationToken.None);
        Assert.NotNull(rotatedKey);
        Assert.False(await store.Store.HeartbeatAsync(server.Id, originalKey,
            new HeartbeatRequest { Players = 0, MaxPlayers = 10 }, CancellationToken.None));
        Assert.True(await store.Store.HeartbeatAsync(server.Id, rotatedKey!,
            new HeartbeatRequest { Players = 0, MaxPlayers = 10 }, CancellationToken.None));

        Assert.True(await store.Store.DeleteOwnedAsync(server.Id, OwnerA, CancellationToken.None));
        Assert.Empty(await store.Store.GetOwnedServersAsync(OwnerA, CancellationToken.None));
    }

    [Fact]
    public async Task PublicFeedDoesNotExposeOwnerOrApiKeyHash()
    {
        using var store = await TestStore.CreateAsync();
        await store.Store.RegisterOwnedAsync(CreateRequest("Public"), OwnerA, CancellationToken.None);

        var server = Assert.Single(await store.Store.GetPublicServersAsync(CancellationToken.None));

        Assert.Null(server.OwnerSteamId64);
        Assert.Null(server.ApiKeyHash);
        var json = JsonSerializer.Serialize(server, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(OwnerA, json, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKeyHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerSteamId64", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyJsonIsImportedOnceAndPreservesOwnerAndApiKeyHash()
    {
        var legacy = new DirectoryDocument
        {
            Servers = new List<DirectoryServer>
            {
                new()
                {
                    Id = "legacy-id", OwnerSteamId64 = OwnerA, Name = "Legacy listing",
                    Address = "legacy.example", Port = 27016, MaxPlayers = 20,
                    ApiKeyHash = ServerDirectoryStore.HashKey("legacy-api-key"),
                    Tags = new List<string> { "roleplay", "legacy" }
                }
            }
        };
        using var store = await TestStore.CreateAsync(legacy);

        var imported = Assert.Single(await store.Store.GetOwnedServersAsync(OwnerA, CancellationToken.None));
        Assert.Equal("Legacy listing", imported.Name);
        Assert.Null(imported.ApiKeyHash);
        Assert.Equal(new[] { "roleplay", "legacy" }, imported.Tags);

        await File.WriteAllTextAsync(store.FilePath, JsonSerializer.Serialize(new DirectoryDocument
        {
            Servers = new List<DirectoryServer> { new() { Id = "late-id", Name = "Should not import" } }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        using var secondInstance = await TestStore.CreateAsync(store.DatabasePath, store.FilePath);
        var publicServers = await secondInstance.Store.GetPublicServersAsync(CancellationToken.None);
        Assert.Single(publicServers);
        Assert.Equal("Legacy listing", Assert.Single(publicServers).Name);
    }

    [Fact]
    public async Task IndependentInstancesCanRegisterConcurrentlyWithoutLostUpdates()
    {
        using var first = await TestStore.CreateAsync();
        using var second = await TestStore.CreateAsync(first.DatabasePath, first.FilePath);

        var registrations = Enumerable.Range(0, 20).Select(index =>
            (index % 2 == 0 ? first.Store : second.Store)
                .RegisterOwnedAsync(CreateRequest($"Concurrent {index}"), OwnerA, CancellationToken.None));

        await Task.WhenAll(registrations);

        Assert.Equal(20, (await first.Store.GetOwnedServersAsync(OwnerA, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task MissingLegacyJsonStartsWithAnEmptyDirectory()
    {
        using var store = await TestStore.CreateAsync();
        Assert.Empty(await store.Store.GetPublicServersAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("steamid")]
    [InlineData("")]
    public async Task OwnerOperationsRejectMalformedSteamId(string invalidSteamId)
    {
        using var store = await TestStore.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.Store.RegisterOwnedAsync(CreateRequest("Invalid"), invalidSteamId, CancellationToken.None));
        Assert.Empty(await store.Store.GetOwnedServersAsync(OwnerA, CancellationToken.None));
    }

    private static RegisterServerRequest CreateRequest(string name) => new()
    {
        Name = name,
        Description = "Test server",
        Address = "server.example",
        Port = 27016,
        MaxPlayers = 32,
        Tags = new List<string> { "roleplay" }
    };

    private sealed class TestStore : IDisposable
    {
        private readonly string _directory;
        public string FilePath { get; }
        public string DatabasePath { get; }
        public ServerDirectoryStore Store { get; }

        private TestStore(string directory, string databasePath, string filePath, ServerDirectoryStore store)
        {
            _directory = directory;
            DatabasePath = databasePath;
            FilePath = filePath;
            Store = store;
        }

        public static async Task<TestStore> CreateAsync(DirectoryDocument? legacy = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "serpf-directory-tests", Guid.NewGuid().ToString("N"));
            return await CreateAsync(Path.Combine(directory, "auth.db"), Path.Combine(directory, "servers.json"), legacy);
        }

        public static async Task<TestStore> CreateAsync(
            string databasePath, string filePath, DirectoryDocument? legacy = null)
        {
            var directory = Path.GetDirectoryName(databasePath)!;
            System.IO.Directory.CreateDirectory(directory);
            if (legacy is not null)
                await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(
                    legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Directory:AuthDatabasePath"] = databasePath,
                    ["Directory:DataPath"] = filePath
                })
                .Build();
            var database = new DirectoryAuthDatabase(configuration);
            await database.InitializeAsync();
            var store = new ServerDirectoryStore(configuration, database);
            await store.InitializeAsync();
            return new TestStore(directory, databasePath, filePath, store);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_directory))
                System.IO.Directory.Delete(_directory, recursive: true);
        }
    }
}
