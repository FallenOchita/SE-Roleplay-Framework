using Xunit;
using SE.Roleplay.Framework.Core.Identity;
using SE.Roleplay.Framework.Core.Persistence;

namespace SE.Roleplay.Framework.Core.Tests;

public sealed class PlayerProfileServiceTests
{
    [Fact]
    public async Task CharacterSelectionSurvivesUnloadAndReload()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            const ulong accountId = 76561198000000001;
            var store = new JsonPlayerDataStore(directory);
            var identities = new PlayerIdentityService();
            identities.Connect(accountId, "TestPilot");

            var profiles = new PlayerProfileService(store, identities, 4);
            var firstLoad = await profiles.LoadAsync(accountId, "TestPilot");
            Assert.Empty(firstLoad.Characters);

            var first = await profiles.CreateCharacterAsync(accountId, "Rhea Voss", "Engineer");
            var second = await profiles.CreateCharacterAsync(accountId, "Marek Sol", "Trader");
            await profiles.SelectCharacterAsync(accountId, second.CharacterId);

            Assert.Equal(second.CharacterId, identities.OnlinePlayers.Single().CharacterId);
            await profiles.UnloadAsync(accountId);
            identities.Disconnect(accountId);

            identities.Connect(accountId, "TestPilot");
            var reloaded = new PlayerProfileService(store, identities, 4);
            var loadedProfile = await reloaded.LoadAsync(accountId, "TestPilot");

            Assert.Equal(2, loadedProfile.Characters.Count);
            Assert.Equal(second.CharacterId, loadedProfile.SelectedCharacterId);
            Assert.Equal(second.CharacterId, identities.OnlinePlayers.Single().CharacterId);
            Assert.Contains(loadedProfile.Characters, character => character.CharacterId == first.CharacterId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AccountCannotExceedCharacterLimit()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            const ulong accountId = 76561198000000002;
            var store = new JsonPlayerDataStore(directory);
            var identities = new PlayerIdentityService();
            identities.Connect(accountId, "LimitTester");
            var profiles = new PlayerProfileService(store, identities, 1);
            await profiles.LoadAsync(accountId, "LimitTester");

            await profiles.CreateCharacterAsync(accountId, "Only Character");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                profiles.CreateCharacterAsync(accountId, "Second Character"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DeletingSelectedCharacterSelectsAnotherOrClearsSelection()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            const ulong accountId = 76561198000000003;
            var identities = new PlayerIdentityService();
            identities.Connect(accountId, "DeleteTester");
            var profiles = new PlayerProfileService(new JsonPlayerDataStore(directory), identities, 3);
            await profiles.LoadAsync(accountId, "DeleteTester");

            var character = await profiles.CreateCharacterAsync(accountId, "Temporary");
            Assert.Equal(character.CharacterId, identities.OnlinePlayers.Single().CharacterId);

            await profiles.DeleteCharacterAsync(accountId, character.CharacterId);

            Assert.Null(identities.OnlinePlayers.Single().CharacterId);
            Assert.Empty(await profiles.GetCharactersAsync(accountId));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task PermissionGrantsSurviveUnloadAndReloadAndSupportWildcard()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            const ulong accountId = 76561198000000004;
            var store = new JsonPlayerDataStore(directory);
            var identities = new PlayerIdentityService();
            identities.Connect(accountId, "PermissionTester");
            var profiles = new PlayerProfileService(store, identities, 3);
            await profiles.LoadAsync(accountId, "PermissionTester");
            await profiles.GrantPermissionAsync(accountId, "admin.kick");
            await profiles.GrantPermissionAsync(accountId, "admin.kick");
            Assert.True(await profiles.HasPermissionAsync(accountId, "ADMIN.KICK"));
            Assert.False(await profiles.HasPermissionAsync(accountId, "admin.ban"));
            await profiles.UnloadAsync(accountId);
            var reloaded = new PlayerProfileService(store, identities, 3);
            await reloaded.LoadAsync(accountId, "PermissionTester");
            Assert.True(await reloaded.HasPermissionAsync(accountId, "admin.kick"));
            await reloaded.RevokePermissionAsync(accountId, "ADMIN.KICK");
            Assert.False(await reloaded.HasPermissionAsync(accountId, "admin.kick"));
            await reloaded.GrantPermissionAsync(accountId, "*");
            Assert.True(await reloaded.HasPermissionAsync(accountId, "anything.here"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task CharacterCanBeRenamedAndDescribedAndChangesPersist()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            const ulong accountId = 76561198000000010;
            var store = new JsonPlayerDataStore(directory);
            var identities = new PlayerIdentityService();
            identities.Connect(accountId, "Editor");
            var profiles = new PlayerProfileService(store, identities, 3);
            await profiles.LoadAsync(accountId, "Editor");
            var character = await profiles.CreateCharacterAsync(accountId, "Old Name", "Old description");

            await profiles.UpdateCharacterAsync(accountId, character.CharacterId, "New Name", "New description");

            var updated = Assert.Single(await profiles.GetCharactersAsync(accountId));
            Assert.Equal("New Name", updated.Name);
            Assert.Equal("New description", updated.Description);
            Assert.True(updated.UpdatedAtUtc >= character.UpdatedAtUtc);

            await profiles.UnloadAsync(accountId);
            var reloaded = new PlayerProfileService(store, identities, 3);
            await reloaded.LoadAsync(accountId, "Editor");
            var persisted = Assert.Single(await reloaded.GetCharactersAsync(accountId));
            Assert.Equal("New Name", persisted.Name);
            Assert.Equal("New description", persisted.Description);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CharacterUpdateRejectsInvalidValuesAndForeignCharacterIds()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            const ulong accountId = 76561198000000011;
            var identities = new PlayerIdentityService();
            identities.Connect(accountId, "Editor");
            var profiles = new PlayerProfileService(new JsonPlayerDataStore(directory), identities, 3);
            await profiles.LoadAsync(accountId, "Editor");
            var character = await profiles.CreateCharacterAsync(accountId, "Valid");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                profiles.UpdateCharacterAsync(accountId, character.CharacterId, " ", ""));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                profiles.UpdateCharacterAsync(accountId, character.CharacterId, "Valid", new string('x', 161)));
            await Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() =>
                profiles.UpdateCharacterAsync(accountId, Guid.NewGuid(), "Other", ""));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }


    [Fact]
    public async Task CharacterMetadataCanBeSetRemovedAndPersisted()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            const ulong accountId = 76561198000000012;
            var identities = new PlayerIdentityService();
            identities.Connect(accountId, "MetadataTester");
            var store = new JsonPlayerDataStore(directory);
            var profiles = new PlayerProfileService(store, identities, 3);
            await profiles.LoadAsync(accountId, "MetadataTester");
            var character = await profiles.CreateCharacterAsync(accountId, "Aster");

            await profiles.SetCharacterMetadataAsync(accountId, character.CharacterId, "Faction", "Independent");
            await profiles.SetCharacterMetadataAsync(accountId, character.CharacterId, "rank.level", "3");
            var saved = Assert.Single(await profiles.GetCharactersAsync(accountId));
            Assert.Equal("Independent", saved.Metadata["faction"]);
            Assert.Equal("3", saved.Metadata["rank.level"]);

            await profiles.UnloadAsync(accountId);
            var reloaded = new PlayerProfileService(store, identities, 3);
            await reloaded.LoadAsync(accountId, "MetadataTester");
            await reloaded.SetCharacterMetadataAsync(accountId, character.CharacterId, "FACTION", null);
            var persisted = Assert.Single(await reloaded.GetCharactersAsync(accountId));
            Assert.False(persisted.Metadata.ContainsKey("faction"));
            Assert.True(persisted.Metadata.ContainsKey("rank.level"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CharacterMetadataRejectsInvalidKeysValuesAndForeignCharacters()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            const ulong accountId = 76561198000000013;
            var identities = new PlayerIdentityService();
            identities.Connect(accountId, "MetadataTester");
            var profiles = new PlayerProfileService(new JsonPlayerDataStore(directory), identities, 3);
            await profiles.LoadAsync(accountId, "MetadataTester");
            var character = await profiles.CreateCharacterAsync(accountId, "Aster");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                profiles.SetCharacterMetadataAsync(accountId, character.CharacterId, "bad key", "value"));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                profiles.SetCharacterMetadataAsync(accountId, character.CharacterId, "valid", new string('x', 257)));
            await Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() =>
                profiles.SetCharacterMetadataAsync(accountId, Guid.NewGuid(), "valid", "value"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SE-RP-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
