using System;
using System.Collections.Generic;
using System.Linq;

namespace SE.Roleplay.Framework.Core.Permissions
{
    /// <summary>In-memory permission grants for the first core milestone; persistence is supplied by the host.</summary>
    public sealed class PermissionService : IPermissionService
    {
        private readonly Dictionary<ulong, HashSet<string>> _grants = new Dictionary<ulong, HashSet<string>>();

        public bool HasPermission(ulong platformId, string permission)
        {
            Validate(permission);
            return _grants.TryGetValue(platformId, out var values) &&
                   (values.Contains(permission) || values.Contains("*"));
        }

        public IReadOnlyCollection<string> GetPermissions(ulong platformId) =>
            _grants.TryGetValue(platformId, out var values) ? values.ToArray() : Array.Empty<string>();

        public void Grant(ulong platformId, string permission)
        {
            Validate(permission);
            if (!_grants.TryGetValue(platformId, out var values))
                _grants[platformId] = values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            values.Add(permission);
        }

        public void Revoke(ulong platformId, string permission)
        {
            Validate(permission);
            if (_grants.TryGetValue(platformId, out var values))
            {
                values.Remove(permission);
                if (values.Count == 0) _grants.Remove(platformId);
            }
        }

        public void Clear(ulong platformId) => _grants.Remove(platformId);

        private static void Validate(string permission)
        {
            if (string.IsNullOrWhiteSpace(permission)) throw new ArgumentException("Permission is required.", nameof(permission));
        }
    }
}
