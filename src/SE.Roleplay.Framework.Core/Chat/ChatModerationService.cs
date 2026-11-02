using System;
using System.Collections.Generic;
using System.Linq;

namespace SE.Roleplay.Framework.Core.Chat
{
    /// <summary>Thread-safe server-owned blocked-phrase rules for public roleplay chat.</summary>
    public sealed class ChatModerationService
    {
        private readonly object _sync = new object();
        private readonly HashSet<string> _blockedPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _enabled = true;

        public bool IsEnabled
        {
            get { lock (_sync) return _enabled; }
            set { lock (_sync) _enabled = value; }
        }

        public IReadOnlyCollection<string> GetBlockedPhrases()
        {
            lock (_sync)
                return _blockedPhrases.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public bool AddBlockedPhrase(string phrase)
        {
            var normalized = NormalizePhrase(phrase);
            lock (_sync)
            {
                if (_blockedPhrases.Count >= 500 && !_blockedPhrases.Contains(normalized))
                    throw new InvalidOperationException("No more than 500 blocked phrases may be configured.");
                return _blockedPhrases.Add(normalized);
            }
        }

        public bool RemoveBlockedPhrase(string phrase)
        {
            var normalized = NormalizePhrase(phrase);
            lock (_sync)
                return _blockedPhrases.Remove(normalized);
        }

        public void ReplaceRules(IEnumerable<string> phrases, bool enabled = true)
        {
            if (phrases == null) throw new ArgumentNullException(nameof(phrases));
            var replacement = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var phrase in phrases)
                replacement.Add(NormalizePhrase(phrase));

            if (replacement.Count > 500)
                throw new ArgumentException("No more than 500 blocked phrases may be configured.", nameof(phrases));

            lock (_sync)
            {
                _blockedPhrases.Clear();
                foreach (var phrase in replacement) _blockedPhrases.Add(phrase);
                _enabled = enabled;
            }
        }

        public bool IsBlocked(string message, out string matchedPhrase)
        {
            matchedPhrase = string.Empty;
            if (string.IsNullOrEmpty(message)) return false;

            string[] phrases;
            lock (_sync)
            {
                if (!_enabled || _blockedPhrases.Count == 0) return false;
                phrases = _blockedPhrases.ToArray();
            }

            foreach (var phrase in phrases)
            {
                if (message.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                matchedPhrase = phrase;
                return true;
            }

            return false;
        }

        private static string NormalizePhrase(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                throw new ArgumentException("A blocked phrase is required.", nameof(phrase));

            var normalized = phrase.Trim();
            if (normalized.Length < 2 || normalized.Length > 80 || normalized.Any(char.IsControl))
                throw new ArgumentException("Blocked phrases must be 2-80 characters and contain no control characters.", nameof(phrase));
            return normalized;
        }
    }
}
