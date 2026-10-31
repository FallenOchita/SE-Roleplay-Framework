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
}
