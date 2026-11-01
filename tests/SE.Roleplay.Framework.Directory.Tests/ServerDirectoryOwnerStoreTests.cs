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
        using var store = new TestStore();
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
        using var store = new TestStore();
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
        using var store = new TestStore();
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
        using var store = new TestStore();
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
    public async Task LegacyJsonWithoutOwnerRemainsOwnerless()
    {
        using var store = new TestStore();
        var legacy = new DirectoryDocument
        {
            Servers = new List<DirectoryServer>
            {
                new() { Id = "legacy-id", Name = "Legacy listing", Address = "legacy.example", Port = 27016, MaxPlayers = 20 }
            }
        };
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        await File.WriteAllTextAsync(store.FilePath, JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Empty(await store.Store.GetOwnedServersAsync(OwnerA, CancellationToken.None));
        Assert.Equal("Legacy listing", Assert.Single(await store.Store.GetPublicServersAsync(CancellationToken.None)).Name);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("steamid")]
    [InlineData("")]
    public async Task OwnerOperationsRejectMalformedSteamId(string invalidSteamId)
    {
        using var store = new TestStore();

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
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "serpf-tests", Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(_directory, "servers.json");
        public ServerDirectoryStore Store { get; }

        public TestStore()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Directory:DataPath"] = FilePath })
                .Build();
            Store = new ServerDirectoryStore(configuration);
            System.IO.Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_directory)) System.IO.Directory.Delete(_directory, recursive: true);
        }
    }
}
