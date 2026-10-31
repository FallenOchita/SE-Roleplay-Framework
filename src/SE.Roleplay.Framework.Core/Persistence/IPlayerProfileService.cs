using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SE.Roleplay.Framework.Core.Persistence
{
    /// <summary>Loads and manages durable player profiles and their roleplay characters.</summary>
    public interface IPlayerProfileService
    {
        Task<PlayerRecord> LoadAsync(ulong platformId, string displayName, CancellationToken cancellationToken = default);
        Task<PlayerCharacter> CreateCharacterAsync(ulong platformId, string name, string description = "", CancellationToken cancellationToken = default);
        Task SelectCharacterAsync(ulong platformId, Guid characterId, CancellationToken cancellationToken = default);
        Task DeleteCharacterAsync(ulong platformId, Guid characterId, CancellationToken cancellationToken = default);
        Task<IReadOnlyCollection<PlayerCharacter>> GetCharactersAsync(ulong platformId, CancellationToken cancellationToken = default);
        Task SaveAsync(ulong platformId, CancellationToken cancellationToken = default);
        Task UnloadAsync(ulong platformId, CancellationToken cancellationToken = default);
        Task SaveAllAsync(CancellationToken cancellationToken = default);
    }
}
