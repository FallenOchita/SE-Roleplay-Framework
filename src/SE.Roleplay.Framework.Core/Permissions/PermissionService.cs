using System;
using System.Collections.Generic;
using System.Linq;

namespace SE.Roleplay.Framework.Core.Permissions
{
    /// <summary>Thread-safe in-memory runtime permission grants; durable grants are owned by player profiles.</summary>
    public sealed class PermissionService : IPermissionService
    {
        private readonly object _sync = new object();
        private readonly Dictionary<ulong, HashSet<string>> _grants = new Dictionary<ulong, HashSet<string>>();

        public bool HasPermission(ulong platformId, string permission)
        {
            Validate(permission);
            lock (_sync)
                return _grants.TryGetValue(platformId, out var values) &&
                    (values.Contains(permission.Trim()) || values.Contains("*"));
        }

        public IReadOnlyCollection<string> GetPermissions(ulong platformId)
        {
            lock (_sync)
                return _grants.TryGetValue(platformId, out var values)
                    ? values.ToArray()
                    : Array.Empty<string>();
        }

        public void Grant(ulong platformId, string permission)
        {
            Validate(permission);
            lock (_sync)
            {
                if (!_grants.TryGetValue(platformId, out var values))
                    _grants[platformId] = values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                values.Add(permission.Trim());
            }
        }

        public void Revoke(ulong platformId, string permission)
        {
            Validate(permission);
            lock (_sync)
            {
                if (_grants.TryGetValue(platformId, out var values))
                {
                    values.Remove(permission.Trim());
                    if (values.Count == 0) _grants.Remove(platformId);
                }
            }
        }

        public void Clear(ulong platformId)
        {
            lock (_sync) _grants.Remove(platformId);
        }

        private static void Validate(string permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
                throw new ArgumentException("Permission is required.", nameof(permission));
        }
    }
}