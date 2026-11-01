using Microsoft.Extensions.Configuration;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class SteamLoginStateStoreTests
{
    private const string BrowserA = "browser-session-id-that-is-at-least-32-chars-A";
    private const string BrowserB = "browser-session-id-that-is-at-least-32-chars-B";

    [Fact]
    public async Task SqliteStore_SharesAndAtomicallyConsumesStateAcrossInstances()
    {
        var path = CreateDatabasePath();
        try
        {
            var database = CreateDatabase(path);
            await database.InitializeAsync();
            var first = new SteamLoginStateStore(database);
            var second = new SteamLoginStateStore(database);
            var state = first.Create(BrowserA, "/oauth/authorize?client_id=serpf-windows-launcher");

            Assert.False(second.TryConsume(state, BrowserB));
            Assert.True(second.TryConsume(state, BrowserA, out var returnPath));
            Assert.Equal("/oauth/authorize?client_id=serpf-windows-launcher", returnPath);
            Assert.False(first.TryConsume(state, BrowserA));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public async Task SqliteStore_OnlyOneInstanceCanConsumeStateConcurrently()
    {
        var path = CreateDatabasePath();
        try
        {
            var database = CreateDatabase(path);
            await database.InitializeAsync();
            var stores = Enumerable.Range(0, 8)
                .Select(_ => new SteamLoginStateStore(database))
                .ToArray();
            var state = stores[0].Create(BrowserA);

            var results = await Task.WhenAll(stores.Select(store =>
                Task.Run(() => store.TryConsume(state, BrowserA))));

            Assert.Single(results, result => result);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Create_ReturnsCryptographicallySizedUrlSafeState()
    {
        var store = new SteamLoginStateStore();

        var state = store.Create(BrowserA);

        Assert.Equal(43, state.Length);
        Assert.DoesNotContain("+", state);
        Assert.DoesNotContain("/", state);
        Assert.DoesNotContain("=", state);
    }

    [Fact]
    public void TryConsume_AcceptsMatchingBrowserSessionOnlyOnce()
    {
        var store = new SteamLoginStateStore();
        var state = store.Create(BrowserA);

        Assert.True(store.TryConsume(state, BrowserA));
        Assert.False(store.TryConsume(state, BrowserA));
    }

    [Fact]
    public void TryConsume_DoesNotAllowAnotherBrowserToConsumeState()
    {
        var store = new SteamLoginStateStore();
        var state = store.Create(BrowserA);

        Assert.False(store.TryConsume(state, BrowserB));
        Assert.True(store.TryConsume(state, BrowserA));
    }

    [Fact]
    public void TryConsume_RejectsExpiredState()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new SteamLoginStateStore(clock);
        var state = store.Create(BrowserA);

        clock.Advance(SteamLoginStateStore.DefaultLifetime);

        Assert.False(store.TryConsume(state, BrowserA));
    }

    [Fact]
    public async Task TryConsume_IsAtomic_WhenSameStateIsConsumedConcurrently()
    {
        var store = new SteamLoginStateStore();
        var state = store.Create(BrowserA);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => store.TryConsume(state, BrowserA))));

        Assert.True(Assert.Single(results, result => result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(null)]
    public void Create_RejectsInvalidBrowserSessionId(string? sessionId)
    {
        var store = new SteamLoginStateStore();

        Assert.Throws<ArgumentException>(() => store.Create(sessionId!));
    }

    [Fact]
    public void TryConsume_ReturnsValidatedLocalAuthorizationReturnPath()
    {
        var store = new SteamLoginStateStore();
        const string returnPath = "/oauth/authorize?client_id=serpf-windows-launcher";
        var state = store.Create(BrowserA, returnPath);

        Assert.True(store.TryConsume(state, BrowserA, out var consumedPath));
        Assert.Equal(returnPath, consumedPath);
    }

    [Theory]
    [InlineData("https://attacker.invalid/steal")]
    [InlineData("//attacker.invalid/steal")]
    [InlineData("/admin")]
    [InlineData("/oauth/authorize\\\\attacker")]
    public void Create_RejectsUnsafeReturnPaths(string returnPath)
    {
        var store = new SteamLoginStateStore();
        Assert.Throws<ArgumentException>(() => store.Create(BrowserA, returnPath));
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
        Path.Combine(Path.GetTempPath(), "serpf-login-state-tests", Guid.NewGuid().ToString("N"), "auth.db");

    private static void DeleteDatabase(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (System.IO.Directory.Exists(directory))
            System.IO.Directory.Delete(directory, recursive: true);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
