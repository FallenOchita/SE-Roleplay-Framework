using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SE.Roleplay.Framework.Core.Permissions
{
    /// <summary>Thread-safe in-memory runtime permission grants; durable grants are owned by player profiles.</summary>
    public sealed class PermissionService : IPermissionService
    {
        private readonly ConcurrentDictionary<ulong, HashSet<string>> _grants =
            new ConcurrentDictionary<ulong, HashSet<string>>();

        public bool HasPermission(ulong platformId, string permission)
        {
            Validate(permission);
            return _grants.TryGetValue(platformId, out var values) &&
                Read(values, set => set.Contains(permission) || set.Contains("*"));
        }

        public IReadOnlyCollection<string> GetPermissions(ulong platformId) =>
            _grants.TryGetValue(platformId, out var values)
                ? Read(values, set => (IReadOnlyCollection<string>)set.ToArray())
                : Array.Empty<string>();

        public void Grant(ulong platformId, string permission)
        {
            Validate(permission);
            var values = _grants.GetOrAdd(platformId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            lock (values) values.Add(permission.Trim());
        }

        public void Revoke(ulong platformId, string permission)
        {
            Validate(permission);
            if (_grants.TryGetValue(platformId, out var values))
            {
                lock (values)
                {
                    values.Remove(permission.Trim());
                    if (values.Count == 0)
                        _grants.TryRemove(new KeyValuePair<ulong, HashSet<string>>(platformId, values));
                }
            }
        }

        public void Clear(ulong platformId) => _grants.TryRemove(platformId, out _);

        private static T Read<T>(HashSet<string> values, Func<HashSet<string>, T> reader)
        {
            lock (values) return reader(values);
        }

        private static void Validate(string permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
                throw new ArgumentException("Permission is required.", nameof(permission));
        }
    }
}