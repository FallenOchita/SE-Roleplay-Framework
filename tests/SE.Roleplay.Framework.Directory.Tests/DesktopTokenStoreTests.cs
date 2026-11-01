using Microsoft.Extensions.Configuration;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class DesktopTokenStoreTests
{
    private const string SteamId = "76561198012345678";
    private const string Client = DesktopTokenStore.RegisteredClientId;


    [Fact]
    public async Task SqliteTokens_AreSharedAcrossInstancesAndReplayRevokesWholeFamily()
    {
        var path = CreateDatabasePath();
        try
        {
            var database = CreateDatabase(path);
            await database.InitializeAsync();
            var first = new DesktopTokenStore(database);
            var second = new DesktopTokenStore(database);
            var initial = first.Issue(SteamId, Client)!;

            Assert.Equal(new DesktopTokenIdentity(SteamId, Client),
                second.ValidateAccessToken(initial.AccessToken, Client));

            var rotated = second.RotateRefreshToken(initial.RefreshToken, Client);
            Assert.NotNull(rotated);
            Assert.Equal(new DesktopTokenIdentity(SteamId, Client),
                first.ValidateAccessToken(rotated.AccessToken, Client));

            Assert.Null(first.RotateRefreshToken(initial.RefreshToken, Client));
            Assert.Null(second.ValidateAccessToken(initial.AccessToken, Client));
            Assert.Null(first.ValidateAccessToken(rotated.AccessToken, Client));
            Assert.Null(second.RotateRefreshToken(rotated.RefreshToken, Client));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public async Task SqliteRefreshRotation_AllowsOnlyOneConcurrentConsumerAcrossInstances()
    {
        var path = CreateDatabasePath();
        try
        {
            var database = CreateDatabase(path);
            await database.InitializeAsync();
            var stores = Enumerable.Range(0, 8).Select(_ => new DesktopTokenStore(database)).ToArray();
            var initial = stores[0].Issue(SteamId, Client)!;

            var results = await Task.WhenAll(stores.Select(store =>
                Task.Run(() => store.RotateRefreshToken(initial.RefreshToken, Client))));

            var winner = Assert.Single(results.Where(result => result is not null))!;
            Assert.Null(stores[1].RotateRefreshToken(initial.RefreshToken, Client));
            Assert.Null(stores[2].ValidateAccessToken(initial.AccessToken, Client));
            Assert.Null(stores[3].ValidateAccessToken(winner.AccessToken, Client));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public async Task SqliteRefreshToken_ExpiresAfterThirtyDays()
    {
        var path = CreateDatabasePath();
        try
        {
            var database = CreateDatabase(path);
            await database.InitializeAsync();
            var clock = new ManualClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
            var store = new DesktopTokenStore(database, clock);
            var pair = store.Issue(SteamId, Client)!;

            clock.Advance(DesktopTokenStore.RefreshTokenLifetime);

            Assert.Null(store.RotateRefreshToken(pair.RefreshToken, Client));
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
        Path.Combine(Path.GetTempPath(), "serpf-desktop-token-tests", Guid.NewGuid().ToString("N"), "auth.db");

    private static void DeleteDatabase(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (System.IO.Directory.Exists(directory))
            System.IO.Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Issue_ReturnsOpaqueBearerTokensAndValidatesAccessIdentity()
    {
        var store = new DesktopTokenStore();

        var pair = store.Issue(SteamId, Client);

        Assert.NotNull(pair);
        Assert.Equal("Bearer", pair.TokenType);
        Assert.Equal((int)DesktopTokenStore.AccessTokenLifetime.TotalSeconds, pair.ExpiresIn);
        Assert.Equal(new DesktopTokenIdentity(SteamId, Client),
            store.ValidateAccessToken(pair.AccessToken, Client));
        Assert.Null(store.ValidateAccessToken(pair.AccessToken, "other-client"));
    }

    [Fact]
    public void Issue_RejectsUnverifiedIdentityAndUnknownClient()
    {
        var store = new DesktopTokenStore();

        Assert.Null(store.Issue("not-a-steam-id", Client));
        Assert.Null(store.Issue(SteamId, "unknown-client"));
    }

    [Fact]
    public void RefreshRotation_IsSingleUseAndOldTokenReplayRevokesFamily()
    {
        var store = new DesktopTokenStore();
        var initial = store.Issue(SteamId, Client)!;

        var rotated = store.RotateRefreshToken(initial.RefreshToken, Client);

        Assert.NotNull(rotated);
        Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken);
        Assert.NotNull(store.ValidateAccessToken(initial.AccessToken, Client));
        Assert.NotNull(store.ValidateAccessToken(rotated.AccessToken, Client));

        Assert.Null(store.RotateRefreshToken(initial.RefreshToken, Client));
        Assert.Null(store.ValidateAccessToken(initial.AccessToken, Client));
        Assert.Null(store.ValidateAccessToken(rotated.AccessToken, Client));
        Assert.Null(store.RotateRefreshToken(rotated.RefreshToken, Client));
    }

    [Fact]
    public async Task RefreshRotation_AllowsOnlyOneConcurrentConsumer()
    {
        var store = new DesktopTokenStore();
        var initial = store.Issue(SteamId, Client)!;

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => store.RotateRefreshToken(initial.RefreshToken, Client))));

        Assert.Single(results.Where(result => result is not null));
        var winner = Assert.Single(results.Where(result => result is not null))!;
        // Replaying the original credential after the race must revoke the winner.
        Assert.Null(store.RotateRefreshToken(initial.RefreshToken, Client));
        Assert.Null(store.ValidateAccessToken(winner.AccessToken, Client));
    }

    [Fact]
    public void AccessToken_ExpiresAfterTenMinutes()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new DesktopTokenStore(clock);
        var pair = store.Issue(SteamId, Client)!;

        clock.Advance(DesktopTokenStore.AccessTokenLifetime);

        Assert.Null(store.ValidateAccessToken(pair.AccessToken, Client));
    }

    [Fact]
    public void RefreshToken_ExpiresAfterThirtyDays()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new DesktopTokenStore(clock);
        var pair = store.Issue(SteamId, Client)!;

        clock.Advance(DesktopTokenStore.RefreshTokenLifetime);

        Assert.Null(store.RotateRefreshToken(pair.RefreshToken, Client));
    }

    [Fact]
    public void RefreshToken_IsBoundToRegisteredClient()
    {
        var store = new DesktopTokenStore();
        var pair = store.Issue(SteamId, Client)!;

        Assert.Null(store.RotateRefreshToken(pair.RefreshToken, "different-client"));
    }

    private sealed class ManualClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
