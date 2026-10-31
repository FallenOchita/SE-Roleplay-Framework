using System;
using System.Collections.Generic;

namespace SE.Roleplay.Framework.Core.Persistence
{
    /// <summary>Persistent roleplay character owned by one platform account.</summary>
    public sealed class PlayerCharacter
    {
        public Guid CharacterId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        internal PlayerCharacter Copy()
        {
            return new PlayerCharacter
            {
                CharacterId = CharacterId,
                Name = Name,
                Description = Description,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = UpdatedAtUtc,
                Metadata = new Dictionary<string, string>(Metadata ?? new Dictionary<string, string>())
            };
        }
    }
}
