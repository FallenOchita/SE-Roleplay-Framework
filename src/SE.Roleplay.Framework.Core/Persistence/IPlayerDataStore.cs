using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SE.Roleplay.Framework.Core.Persistence
{
    /// <summary>Storage boundary; implementations may use files or a database in a server adapter.</summary>
    public interface IPlayerDataStore
    {
        Task<PlayerRecord?> LoadAsync(ulong platformId, CancellationToken cancellationToken = default);
        Task SaveAsync(PlayerRecord record, CancellationToken cancellationToken = default);
        Task<IReadOnlyCollection<ulong>> GetKnownPlayersAsync(CancellationToken cancellationToken = default);
    }
}
