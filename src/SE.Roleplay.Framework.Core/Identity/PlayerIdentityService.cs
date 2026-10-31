using System;
using System.Collections.Generic;

namespace SE.Roleplay.Framework.Core.Identity
{
    /// <summary>Session identity registry. Durable character data belongs in persistence.</summary>
    public sealed class PlayerIdentityService : IPlayerIdentityService
    {
        private readonly Dictionary<ulong, PlayerIdentity> _players = new Dictionary<ulong, PlayerIdentity>();
        public IReadOnlyCollection<PlayerIdentity> OnlinePlayers => _players.Values;

        public bool TryGet(ulong platformId, out PlayerIdentity identity) => _players.TryGetValue(platformId, out identity!);

        public void Connect(ulong platformId, string displayName)
        {
            if (_players.ContainsKey(platformId)) throw new InvalidOperationException("Player is already connected.");
            _players.Add(platformId, new PlayerIdentity(platformId, displayName));
        }

        public void Disconnect(ulong platformId) => _players.Remove(platformId);

        public void UpdateDisplayName(ulong platformId, string displayName)
        {
            if (!_players.TryGetValue(platformId, out var current)) throw new KeyNotFoundException("Player is not connected.");
            _players[platformId] = new PlayerIdentity(platformId, displayName, current.CharacterId);
        }
    }
}
