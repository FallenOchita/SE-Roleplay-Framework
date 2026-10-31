using SE.Roleplay.Framework.Core.Events;
using SE.Roleplay.Framework.Core.Identity;
using SE.Roleplay.Framework.Core.Persistence;
using Xunit;

namespace SE.Roleplay.Framework.Core.Tests;

public sealed class PlayerSessionServiceTests
{
    [Fact]
    public async Task ConnectLoadsProfileBeforePublishingConnectedEventAndDisconnectSaves()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SE-RP-Session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const ulong accountId = 76561198000000005;
            var store = new JsonPlayerDataStore(directory);
            var identities = new PlayerIdentityService();
            var profiles = new PlayerProfileService(store, identities, 4);
            var events = new EventBus();
            var session = new PlayerSessionService(identities, profiles, events);
            var connectedEventSawProfile = false;
            using var subscription = events.Subscribe<PlayerConnected>(_ =>
            {
                connectedEventSawProfile = profiles.GetCharactersAsync(accountId).GetAwaiter().GetResult().Count == 0;
            });

            await session.ConnectAsync(accountId, "SessionTester");
            Assert.True(connectedEventSawProfile);
            var character = await profiles.CreateCharacterAsync(accountId, "Saved Pilot");
            await session.DisconnectAsync(accountId);

            Assert.False(identities.TryGet(accountId, out _));
            var loaded = await store.LoadAsync(accountId);
            Assert.NotNull(loaded);
            Assert.Equal(character.CharacterId, Assert.Single(loaded!.Characters).CharacterId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
