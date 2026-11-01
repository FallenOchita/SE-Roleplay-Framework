using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SE.Roleplay.Framework.Directory.Models;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class PlayerProfileStoreTests
{
    private const string Owner = "76561198012345678";
    private const string Player = "76561198087654321";

    [Fact]
    public async Task ProfilePersistsAcrossStoreInstancesAndIsBoundToAuthenticatedSteamId()
    {
        using var fixture = await Fixture.CreateAsync();
        var first = new PlayerProfileStore(fixture.Database, TimeProvider.System);
        var second = new PlayerProfileStore(fixture.Database, TimeProvider.System);

        var saved = await first.UpdateProfileAsync(Player,
            new UpdatePlayerProfileRequest { DisplayName = "  Pilot One  ", Biography = "  Roleplay builder  " },
            CancellationToken.None);
        var loaded = await second.GetProfileAsync(Player, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("Pilot One", saved.DisplayName);
        Assert.Equal("Roleplay builder", loaded!.Biography);
        Assert.Equal(saved.UpdatedAtUtc, loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task PermissionsAreServerScopedOwnerControlledAndRevocable()
    {
        using var fixture = await Fixture.CreateAsync();
        var (serverA, _) = await fixture.Servers.RegisterOwnedAsync(CreateServerRequest("A"), Owner, CancellationToken.None);
        var (serverB, _) = await fixture.Servers.RegisterOwnedAsync(CreateServerRequest("B"), Player, CancellationToken.None);
        var profiles = new PlayerProfileStore(fixture.Database, TimeProvider.System);

        Assert.False(await profiles.GrantPermissionAsync(serverA.Id, Player, Owner, "staff.moderate", null, CancellationToken.None));
        Assert.True(await profiles.GrantPermissionAsync(serverA.Id, Owner, Player, "staff.moderate", null, CancellationToken.None));
        Assert.True(await profiles.HasPermissionAsync(serverA.Id, Player, "staff.moderate", CancellationToken.None));
        Assert.False(await profiles.HasPermissionAsync(serverB.Id, Player, "staff.moderate", CancellationToken.None));
        Assert.False(await profiles.RevokePermissionAsync(serverA.Id, Player, Player, "staff.moderate", CancellationToken.None));
        Assert.True(await profiles.RevokePermissionAsync(serverA.Id, Owner, Player, "staff.moderate", CancellationToken.None));
        Assert.False(await profiles.HasPermissionAsync(serverA.Id, Player, "staff.moderate", CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredPermissionIsNotReturnedOrEffective()
    {
        using var fixture = await Fixture.CreateAsync();
        var (server, _) = await fixture.Servers.RegisterOwnedAsync(CreateServerRequest("Expiry"), Owner, CancellationToken.None);
        var profiles = new PlayerProfileStore(fixture.Database, TimeProvider.System);

        Assert.False(await profiles.GrantPermissionAsync(server.Id, Owner, Player, "role.moderator",
            DateTime.UtcNow.AddMinutes(-1), CancellationToken.None));
        Assert.True(await profiles.GrantPermissionAsync(server.Id, Owner, Player, "role.moderator",
            DateTime.UtcNow.AddMinutes(10), CancellationToken.None));
        Assert.Single(await profiles.GetPermissionsAsync(server.Id, Player, CancellationToken.None));
    }

    [Fact]
    public async Task ServerAllowlistIsPersistentAndOnlyTheOwnerCanManageIt()
    {
        using var fixture = await Fixture.CreateAsync();
        var (server, _) = await fixture.Servers.RegisterOwnedAsync(CreateServerRequest("Allowlist"), Owner, CancellationToken.None);
        var memberships = new ServerMembershipStore(fixture.Database, TimeProvider.System);

        Assert.False((await memberships.GetStatusAsync(server.Id, Player, CancellationToken.None)).IsAllowed);
        Assert.False(await memberships.AddMemberAsync(server.Id, Player, Owner, "unauthorized", CancellationToken.None));
        Assert.True(await memberships.AddMemberAsync(server.Id, Owner, Player, "approved roleplay member", CancellationToken.None));
        Assert.True((await memberships.GetStatusAsync(server.Id, Player, CancellationToken.None)).IsAllowed);
        Assert.Single(await memberships.GetMembersAsync(server.Id, Owner, CancellationToken.None));
        Assert.Empty(await memberships.GetMembersAsync(server.Id, Player, CancellationToken.None));

        var secondInstance = new ServerMembershipStore(fixture.Database, TimeProvider.System);
        Assert.True((await secondInstance.GetStatusAsync(server.Id, Player, CancellationToken.None)).IsAllowed);
        Assert.False(await secondInstance.RemoveMemberAsync(server.Id, Player, Player, CancellationToken.None));
        Assert.True(await secondInstance.RemoveMemberAsync(server.Id, Owner, Player, CancellationToken.None));
        Assert.False((await memberships.GetStatusAsync(server.Id, Player, CancellationToken.None)).IsAllowed);
    }

    [Fact]
    public async Task ServerAllowlistRejectsInvalidSteamIdsAndOversizedNotes()
    {
        using var fixture = await Fixture.CreateAsync();
        var (server, _) = await fixture.Servers.RegisterOwnedAsync(CreateServerRequest("Validation"), Owner, CancellationToken.None);
        var memberships = new ServerMembershipStore(fixture.Database, TimeProvider.System);

        Assert.False(await memberships.AddMemberAsync(server.Id, Owner, "bad-id", "", CancellationToken.None));
        Assert.False(await memberships.AddMemberAsync(server.Id, Owner, Player, new string('x', 201), CancellationToken.None));
    }

    [Fact]
    public async Task ServerAdmissionRequiresValidServerKeyAndFailsClosedWhenAllowlistEnabled()
    {
        using var fixture = await Fixture.CreateAsync();
        var (server, apiKey) = await fixture.Servers.RegisterOwnedAsync(CreateServerRequest("Admission"), Owner, CancellationToken.None);
        var memberships = new ServerMembershipStore(fixture.Database, TimeProvider.System);

        var disabledDefault = await memberships.GetAdmissionDecisionAsync(server.Id, apiKey, Player, CancellationToken.None);
        Assert.NotNull(disabledDefault);
        Assert.False(disabledDefault.AllowlistEnabled);
        Assert.True(disabledDefault.ShouldAdmit);
        Assert.Null(await memberships.GetAdmissionDecisionAsync(server.Id, "wrong-key", Player, CancellationToken.None));

        Assert.True(await memberships.SetAllowlistEnabledAsync(server.Id, Owner, true, CancellationToken.None));
        var denied = await memberships.GetAdmissionDecisionAsync(server.Id, apiKey, Player, CancellationToken.None);
        Assert.NotNull(denied);
        Assert.True(denied.AllowlistEnabled);
        Assert.False(denied.IsAllowed);
        Assert.False(denied.ShouldAdmit);

        Assert.True(await memberships.AddMemberAsync(server.Id, Owner, Player, "approved", CancellationToken.None));
        var admitted = await memberships.GetAdmissionDecisionAsync(server.Id, apiKey, Player, CancellationToken.None);
        Assert.NotNull(admitted);
        Assert.True(admitted.IsAllowed);
        Assert.True(admitted.ShouldAdmit);

        Assert.False(await memberships.SetAllowlistEnabledAsync(server.Id, Player, false, CancellationToken.None));
        Assert.True(await memberships.SetAllowlistEnabledAsync(server.Id, Owner, false, CancellationToken.None));
        var disabledAgain = await memberships.GetAdmissionDecisionAsync(server.Id, apiKey, Player, CancellationToken.None);
        Assert.True(disabledAgain!.ShouldAdmit);
    }

    [Fact]
    public async Task InvalidProfileAndPermissionInputsAreRejected()
    {
        using var fixture = await Fixture.CreateAsync();
        var profiles = new PlayerProfileStore(fixture.Database, TimeProvider.System);

        Assert.Null(await profiles.UpdateProfileAsync("not-a-steamid",
            new UpdatePlayerProfileRequest { DisplayName = "Pilot" }, CancellationToken.None));
        Assert.Null(await profiles.UpdateProfileAsync(Player,
            new UpdatePlayerProfileRequest { DisplayName = new string('x', 41) }, CancellationToken.None));
        Assert.False(await profiles.GrantPermissionAsync("server", Owner, Player, "Staff.Admin", null, CancellationToken.None));
    }

    private static RegisterServerRequest CreateServerRequest(string name) => new()
    {
        Name = name, Address = "127.0.0.1", Port = 27016, MaxPlayers = 20
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory;
        public DirectoryAuthDatabase Database { get; }
        public ServerDirectoryStore Servers { get; }

        private Fixture(string directory, DirectoryAuthDatabase database, ServerDirectoryStore servers)
        {
            _directory = directory;
            Database = database;
            Servers = servers;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "serpf-player-tests-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Directory:AuthDatabasePath"] = Path.Combine(directory, "auth.db"),
                ["Directory:DataPath"] = Path.Combine(directory, "legacy.json")
            }).Build();
            var database = new DirectoryAuthDatabase(configuration);
            await database.InitializeAsync();
            var servers = new ServerDirectoryStore(configuration, database);
            await servers.InitializeAsync();
            return new Fixture(directory, database, servers);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_directory)) System.IO.Directory.Delete(_directory, recursive: true);
        }
    }
}
