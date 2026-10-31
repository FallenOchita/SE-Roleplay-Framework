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

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SE-RP-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
