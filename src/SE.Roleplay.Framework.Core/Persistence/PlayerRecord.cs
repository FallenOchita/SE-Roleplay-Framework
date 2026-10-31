using System;
using System.Collections.Generic;

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
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
