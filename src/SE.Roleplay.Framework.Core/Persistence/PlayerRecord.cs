using System;
using System.Collections.Generic;
using System.Linq;

namespace SE.Roleplay.Framework.Core.Persistence
{
    /// <summary>Serializable player profile data. Keep game API objects out of this model.</summary>
    public sealed class PlayerRecord
    {
        public ulong PlatformId { get; set; }
        public string LastKnownName { get; set; } = string.Empty;
        public Guid? SelectedCharacterId { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public List<string> Permissions { get; set; } = new List<string>();
        public List<PlayerCharacter> Characters { get; set; } = new List<PlayerCharacter>();
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        internal PlayerRecord Copy()
        {
            return new PlayerRecord
            {
                PlatformId = PlatformId,
                LastKnownName = LastKnownName,
                SelectedCharacterId = SelectedCharacterId,
                Metadata = new Dictionary<string, string>(Metadata ?? new Dictionary<string, string>()),
                Permissions = new List<string>(Permissions ?? new List<string>()),
                Characters = (Characters ?? new List<PlayerCharacter>()).Where(c => c != null).Select(c => c.Copy()).ToList(),
                UpdatedAtUtc = UpdatedAtUtc
            };
        }
    }
}
