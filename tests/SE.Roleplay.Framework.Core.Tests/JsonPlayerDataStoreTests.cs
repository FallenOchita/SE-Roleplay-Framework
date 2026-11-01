using Xunit;
using SE.Roleplay.Framework.Core.Persistence;

namespace SE.Roleplay.Framework.Core.Tests;

public sealed class JsonPlayerDataStoreTests
{
    [Fact]
    public async Task StoreRoundTripsProfilesAndListsKnownAccounts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SE-RP-Store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonPlayerDataStore(directory);
            var accountId = 76561198000000004UL;
            var characterId = Guid.NewGuid();
            await store.SaveAsync(new PlayerRecord
            {
                PlatformId = accountId,
                LastKnownName = "StorageTester",
                SelectedCharacterId = characterId,
                Characters = new List<PlayerCharacter>
                {
                    new PlayerCharacter { CharacterId = characterId, Name = "Aster Vale", Description = "Pilot" }
                }
            });

            var loaded = await store.LoadAsync(accountId);
            Assert.NotNull(loaded);
            Assert.Equal("StorageTester", loaded!.LastKnownName);
            Assert.Equal(characterId, loaded.SelectedCharacterId);
            Assert.Equal("Aster Vale", Assert.Single(loaded.Characters).Name);
            Assert.Contains(accountId, await store.GetKnownPlayersAsync());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SameSteamAccountHasIndependentCharactersOnDifferentServers()
    {
        var root = Path.Combine(Path.GetTempPath(), "SE-RP-Server-Isolation-" + Guid.NewGuid().ToString("N"));
        var serverA = Path.Combine(root, "ServerA", "Storage", "SE-Roleplay-Framework", "Players");
        var serverB = Path.Combine(root, "ServerB", "Storage", "SE-Roleplay-Framework", "Players");
        const ulong accountId = 76561198000000004UL;

        try
        {
            var storeA = new JsonPlayerDataStore(serverA);
            var storeB = new JsonPlayerDataStore(serverB);
            await storeA.SaveAsync(new PlayerRecord
            {
                PlatformId = accountId,
                LastKnownName = "SharedSteamAccount",
                Characters = new List<PlayerCharacter>
                {
                    new PlayerCharacter { Name = "Server A Character" }
                }
            });
            await storeB.SaveAsync(new PlayerRecord
            {
                PlatformId = accountId,
                LastKnownName = "SharedSteamAccount",
                Characters = new List<PlayerCharacter>
                {
                    new PlayerCharacter { Name = "Server B Character" }
                }
            });

            var profileA = await storeA.LoadAsync(accountId);
            var profileB = await storeB.LoadAsync(accountId);

            Assert.Equal("Server A Character", Assert.Single(profileA!.Characters).Name);
            Assert.Equal("Server B Character", Assert.Single(profileB!.Characters).Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
