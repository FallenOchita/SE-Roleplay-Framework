using System.Collections.Generic;

namespace SE.Roleplay.Framework.Core.Identity
{
    public interface IPlayerIdentityService
    {
        bool TryGet(ulong platformId, out PlayerIdentity identity);
        IReadOnlyCollection<PlayerIdentity> OnlinePlayers { get; }
        void Connect(ulong platformId, string displayName);
        void Disconnect(ulong platformId);
        void UpdateDisplayName(ulong platformId, string displayName);
    }
}
