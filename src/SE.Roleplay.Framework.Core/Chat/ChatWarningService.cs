using System;
using System.Collections.Generic;
using System.Linq;

namespace SE.Roleplay.Framework.Core.Chat
{
    /// <summary>Thread-safe, server-owned audit trail of moderator-issued player chat warnings.</summary>
    public sealed class ChatWarningService
    {
        public const int MaximumWarningsPerPlayer = 100;
        public const int MaximumWarningsTotal = 5000;
        private readonly object _sync = new object();
        private readonly List<ChatWarningRecord> _warnings = new List<ChatWarningRecord>();

        public ChatWarningRecord AddWarning(ulong playerSteamId, ulong moderatorSteamId, string reason, DateTimeOffset? createdAt = null)
        {
            if (playerSteamId == 0) throw new ArgumentOutOfRangeException(nameof(playerSteamId));
            if (moderatorSteamId == 0) throw new ArgumentOutOfRangeException(nameof(moderatorSteamId));
            var normalized = NormalizeReason(reason);
            lock (_sync)
            {
                if (_warnings.Count >= MaximumWarningsTotal)
                    throw new InvalidOperationException("The warning ledger has reached its 5,000-record limit.");
                if (_warnings.Count(item => item.PlayerSteamId == playerSteamId) >= MaximumWarningsPerPlayer)
                    throw new InvalidOperationException("This player has reached the 100-warning limit.");

                var record = new ChatWarningRecord(
                    Guid.NewGuid().ToString("N"),
                    playerSteamId,
                    moderatorSteamId,
                    normalized,
                    createdAt ?? DateTimeOffset.UtcNow);
                _warnings.Add(record);
                return record;
            }
        }

        public IReadOnlyList<ChatWarningRecord> GetWarnings(ulong playerSteamId)
        {
            lock (_sync)
                return _warnings.Where(item => item.PlayerSteamId == playerSteamId)
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .ToArray();
        }

        public IReadOnlyList<ChatWarningRecord> GetRecentWarnings(int count)
        {
            if (count < 1 || count > 100) throw new ArgumentOutOfRangeException(nameof(count));
            lock (_sync)
                return _warnings.OrderByDescending(item => item.CreatedAtUtc).Take(count).ToArray();
        }

        public int ClearWarnings(ulong playerSteamId)
        {
            lock (_sync)
                return _warnings.RemoveAll(item => item.PlayerSteamId == playerSteamId);
        }

        public IReadOnlyList<ChatWarningRecord> Export()
        {
            lock (_sync) return _warnings.ToArray();
        }

        public void Replace(IEnumerable<ChatWarningRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var replacement = records.ToList();
            if (replacement.Count > MaximumWarningsTotal)
                throw new ArgumentException("The warning ledger cannot contain more than 5,000 records.", nameof(records));
            if (replacement.Any(item => item == null || item.PlayerSteamId == 0 || item.ModeratorSteamId == 0 ||
                string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Reason) ||
                item.Reason.Length > 240 || item.Reason.Any(char.IsControl)))
                throw new ArgumentException("The warning ledger contains an invalid record.", nameof(records));
            if (replacement.GroupBy(item => item.PlayerSteamId).Any(group => group.Count() > MaximumWarningsPerPlayer))
                throw new ArgumentException("A player cannot have more than 100 stored warnings.", nameof(records));

            lock (_sync)
            {
                _warnings.Clear();
                _warnings.AddRange(replacement);
            }
        }

        private static string NormalizeReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A warning reason is required.", nameof(reason));
            var normalized = reason.Trim();
            if (normalized.Length < 3 || normalized.Length > 240 || normalized.Any(char.IsControl))
                throw new ArgumentException("Warning reasons must be 3-240 characters and contain no control characters.", nameof(reason));
            return normalized;
        }
    }

    public sealed class ChatWarningRecord
    {
        public ChatWarningRecord(string id, ulong playerSteamId, ulong moderatorSteamId, string reason, DateTimeOffset createdAtUtc)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            PlayerSteamId = playerSteamId;
            ModeratorSteamId = moderatorSteamId;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
            CreatedAtUtc = createdAtUtc.ToUniversalTime();
        }

        public string Id { get; }
        public ulong PlayerSteamId { get; }
        public ulong ModeratorSteamId { get; }
        public string Reason { get; }
        public DateTimeOffset CreatedAtUtc { get; }
    }
}
