using System;

namespace SE.Roleplay.Framework.Core.Identity
{
    /// <summary>Stable framework identity associated with a platform account.</summary>
    public sealed class PlayerIdentity
    {
        public PlayerIdentity(ulong platformId, string displayName, Guid? characterId = null)
        {
            if (platformId == 0) throw new ArgumentOutOfRangeException(nameof(platformId));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A display name is required.", nameof(displayName));
            PlatformId = platformId;
            DisplayName = displayName.Trim();
            CharacterId = characterId;
        }

        public ulong PlatformId { get; }
        public string DisplayName { get; }
        public Guid? CharacterId { get; private set; }

        public void SelectCharacter(Guid? characterId) => CharacterId = characterId;
    }
}
