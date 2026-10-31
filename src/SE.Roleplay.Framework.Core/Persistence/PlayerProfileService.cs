using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SE.Roleplay.Framework.Core.Identity;

namespace SE.Roleplay.Framework.Core.Persistence
{
    /// <summary>
    /// In-memory profile cache backed by the configured player data store.
    /// Each account is serialized independently so separate players can save concurrently.
    /// </summary>
    public sealed class PlayerProfileService : IPlayerProfileService
    {
        private readonly IPlayerDataStore _store;
        private readonly IPlayerIdentityService _identities;
        private readonly int _maximumCharacters;
        private readonly ConcurrentDictionary<ulong, PlayerRecord> _profiles = new ConcurrentDictionary<ulong, PlayerRecord>();
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new ConcurrentDictionary<ulong, SemaphoreSlim>();

        public PlayerProfileService(IPlayerDataStore store, IPlayerIdentityService identities, int maximumCharactersPerPlayer)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _identities = identities ?? throw new ArgumentNullException(nameof(identities));
            if (maximumCharactersPerPlayer < 1) throw new ArgumentOutOfRangeException(nameof(maximumCharactersPerPlayer));
            _maximumCharacters = maximumCharactersPerPlayer;
        }

        public async Task<PlayerRecord> LoadAsync(ulong platformId, string displayName, CancellationToken cancellationToken = default)
        {
            ValidateAccount(platformId);
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A display name is required.", nameof(displayName));
            var gate = GetLock(platformId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_profiles.TryGetValue(platformId, out var cached))
                {
                    cached.LastKnownName = displayName.Trim();
                    cached.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(cached, cancellationToken).ConfigureAwait(false);
                    if (_identities.TryGet(platformId, out var cachedIdentity))
                        cachedIdentity.SelectCharacter(cached.SelectedCharacterId);
                    return cached.Copy();
                }

                var record = await _store.LoadAsync(platformId, cancellationToken).ConfigureAwait(false)
                    ?? new PlayerRecord { PlatformId = platformId };
                if (record.PlatformId != 0 && record.PlatformId != platformId)
                    throw new InvalidOperationException("Stored player profile account ID does not match its requested key.");

                record.PlatformId = platformId;
                record.LastKnownName = displayName.Trim();
                record.Characters = record.Characters ?? new List<PlayerCharacter>();
                record.Metadata = record.Metadata ?? new Dictionary<string, string>();
                record.Permissions = record.Permissions ?? new List<string>();
                NormalizeCharacters(record);
                if (record.SelectedCharacterId.HasValue &&
                    !record.Characters.Any(c => c.CharacterId == record.SelectedCharacterId.Value))
                    record.SelectedCharacterId = null;

                record.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
                _profiles[platformId] = record;

                if (_identities.TryGet(platformId, out var identity))
                    identity.SelectCharacter(record.SelectedCharacterId);
                return record.Copy();
            }
            finally { gate.Release(); }
        }

        public async Task<PlayerCharacter> CreateCharacterAsync(ulong platformId, string name, string description = "", CancellationToken cancellationToken = default)
        {
            var gate = GetLock(platformId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var record = GetLoadedProfile(platformId);
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A character name is required.", nameof(name));
                if (record.Characters.Count >= _maximumCharacters)
                    throw new InvalidOperationException("This account has reached its character limit.");

                var character = new PlayerCharacter
                {
                    CharacterId = Guid.NewGuid(),
                    Name = name.Trim(),
                    Description = description?.Trim() ?? string.Empty
                };
                record.Characters.Add(character);
                if (!record.SelectedCharacterId.HasValue)
                    SetSelectedCharacter(record, character.CharacterId);
                await SaveRecordAsync(record, cancellationToken).ConfigureAwait(false);
                return character.Copy();
            }
            finally { gate.Release(); }
        }

        public async Task SelectCharacterAsync(ulong platformId, Guid characterId, CancellationToken cancellationToken = default)
        {
            var gate = GetLock(platformId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var record = GetLoadedProfile(platformId);
                if (!record.Characters.Any(c => c.CharacterId == characterId))
                    throw new KeyNotFoundException("The requested character does not belong to this account.");
                SetSelectedCharacter(record, characterId);
                await SaveRecordAsync(record, cancellationToken).ConfigureAwait(false);
            }
            finally { gate.Release(); }
        }

        public async Task DeleteCharacterAsync(ulong platformId, Guid characterId, CancellationToken cancellationToken = default)
        {
            var gate = GetLock(platformId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var record = GetLoadedProfile(platformId);
                var character = record.Characters.FirstOrDefault(c => c.CharacterId == characterId);
                if (character == null) throw new KeyNotFoundException("The requested character does not belong to this account.");
                record.Characters.Remove(character);
                if (record.SelectedCharacterId == characterId)
                    SetSelectedCharacter(record, record.Characters.FirstOrDefault()?.CharacterId);
                await SaveRecordAsync(record, cancellationToken).ConfigureAwait(false);
            }
            finally { gate.Release(); }
        }

        public async Task<IReadOnlyCollection<PlayerCharacter>> GetCharactersAsync(ulong platformId, CancellationToken cancellationToken = default)
        {
            var gate = GetLock(platformId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return GetLoadedProfile(platformId).Characters.Select(c => c.Copy()).ToArray(); }
            finally { gate.Release(); }
        }

        public async Task SaveAsync(ulong platformId, CancellationToken cancellationToken = default)
        {
            var gate = GetLock(platformId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await SaveRecordAsync(GetLoadedProfile(platformId), cancellationToken).ConfigureAwait(false); }
            finally { gate.Release(); }
        }

        public async Task UnloadAsync(ulong platformId, CancellationToken cancellationToken = default)
        {
            var gate = GetLock(platformId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_profiles.TryGetValue(platformId, out var record))
                {
                    await SaveRecordAsync(record, cancellationToken).ConfigureAwait(false);
                    _profiles.TryRemove(platformId, out _);
                }
            }
            finally { gate.Release(); }
        }

        public async Task SaveAllAsync(CancellationToken cancellationToken = default)
        {
            foreach (var platformId in _profiles.Keys.ToArray())
                await SaveAsync(platformId, cancellationToken).ConfigureAwait(false);
        }

        private async Task SaveRecordAsync(PlayerRecord record, CancellationToken cancellationToken)
        {
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            foreach (var character in record.Characters)
                character.UpdatedAtUtc = character.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : character.UpdatedAtUtc;
            await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }

        private PlayerRecord GetLoadedProfile(ulong platformId)
        {
            ValidateAccount(platformId);
            if (!_profiles.TryGetValue(platformId, out var record))
                throw new InvalidOperationException("The player profile must be loaded before managing characters.");
            return record;
        }

        private void SetSelectedCharacter(PlayerRecord record, Guid? characterId)
        {
            record.SelectedCharacterId = characterId;
            if (_identities.TryGet(record.PlatformId, out var identity))
                identity.SelectCharacter(characterId);
        }

        private static void NormalizeCharacters(PlayerRecord record)
        {
            var ids = new HashSet<Guid>();
            record.Characters = record.Characters
                .Where(c => c != null && c.CharacterId != Guid.Empty && !string.IsNullOrWhiteSpace(c.Name) && ids.Add(c.CharacterId))
                .ToList();
            foreach (var character in record.Characters)
            {
                character.Metadata = character.Metadata ?? new Dictionary<string, string>();
                character.Name = character.Name.Trim();
                character.Description = character.Description ?? string.Empty;
            }
        }

        private SemaphoreSlim GetLock(ulong platformId) =>
            _locks.GetOrAdd(platformId, _ => new SemaphoreSlim(1, 1));

        private static void ValidateAccount(ulong platformId)
        {
            if (platformId == 0) throw new ArgumentOutOfRangeException(nameof(platformId));
        }
    }
}
