using System;
using System.Threading;
using System.Threading.Tasks;
using SE.Roleplay.Framework.Core.Events;
using SE.Roleplay.Framework.Core.Persistence;

namespace SE.Roleplay.Framework.Core.Identity
{
    /// <summary>Coordinates connection lifecycle with automatic profile load/save.</summary>
    public sealed class PlayerSessionService
    {
        private readonly IPlayerIdentityService _identities;
        private readonly IPlayerProfileService _profiles;
        private readonly IEventBus _events;

        public PlayerSessionService(IPlayerIdentityService identities, IPlayerProfileService profiles, IEventBus events)
        {
            _identities = identities ?? throw new ArgumentNullException(nameof(identities));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public async Task<PlayerRecord> ConnectAsync(ulong platformId, string displayName, CancellationToken cancellationToken = default)
        {
            _identities.Connect(platformId, displayName);
            PlayerRecord profile;
            try
            {
                profile = await _profiles.LoadAsync(platformId, displayName, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _identities.Disconnect(platformId);
                throw;
            }

            // Profile loading is complete before listeners are notified.
            _events.Publish(new PlayerConnected(platformId, displayName));
            return profile;
        }

        public async Task DisconnectAsync(ulong platformId, CancellationToken cancellationToken = default)
        {
            if (!_identities.TryGet(platformId, out _)) return;
            try
            {
                await _profiles.UnloadAsync(platformId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _identities.Disconnect(platformId);
                _events.Publish(new PlayerDisconnected(platformId));
            }
        }
    }
}
