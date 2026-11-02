using System;
using System.Threading.Tasks;
using SE.Roleplay.Framework.Core.Permissions;
using Xunit;

namespace SE.Roleplay.Framework.Core.Tests;

public sealed class PermissionServiceTests
{
    [Fact]
    public void GrantsAreCaseInsensitiveAndWildcardAppliesToAllPermissions()
    {
        var service = new PermissionService();
        const ulong playerId = 76561198000000006;

        service.Grant(playerId, "Admin.Kick");

        Assert.True(service.HasPermission(playerId, "admin.kick"));
        Assert.False(service.HasPermission(playerId, "admin.ban"));

        service.Grant(playerId, "*");

        Assert.True(service.HasPermission(playerId, "anything.at.all"));
        Assert.Contains("*", service.GetPermissions(playerId));
    }

    [Fact]
    public void RevokeAndClearRemoveRuntimeGrants()
    {
        var service = new PermissionService();
        const ulong playerId = 76561198000000007;
        service.Grant(playerId, "rp.chat");
        service.Grant(playerId, "admin.kick");

        service.Revoke(playerId, "RP.CHAT");
        Assert.False(service.HasPermission(playerId, "rp.chat"));
        Assert.True(service.HasPermission(playerId, "admin.kick"));

        service.Clear(playerId);
        Assert.Empty(service.GetPermissions(playerId));
    }

    [Fact]
    public async Task ConcurrentUpdatesRemainSafe()
    {
        var service = new PermissionService();
        const ulong playerId = 76561198000000008;

        await Task.WhenAll(
            Task.Run(() => service.Grant(playerId, "rp.chat")),
            Task.Run(() => service.Grant(playerId, "admin.kick")),
            Task.Run(() => service.GetPermissions(playerId)),
            Task.Run(() => service.HasPermission(playerId, "rp.chat")));

        Assert.Contains("rp.chat", service.GetPermissions(playerId));
        Assert.Contains("admin.kick", service.GetPermissions(playerId));
    }

    [Fact]
    public void BlankPermissionIsRejected()
    {
        var service = new PermissionService();
        Assert.Throws<ArgumentException>(() => service.Grant(76561198000000009, " "));
    }
}
