using System.Collections.Generic;

namespace SE.Roleplay.Framework.Core.Permissions
{
    public interface IPermissionService
    {
        bool HasPermission(ulong platformId, string permission);
        IReadOnlyCollection<string> GetPermissions(ulong platformId);
        void Grant(ulong platformId, string permission);
        void Revoke(ulong platformId, string permission);
        void Clear(ulong platformId);
    }
}
