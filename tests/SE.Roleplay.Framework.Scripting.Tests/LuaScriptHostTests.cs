using System.Collections.Generic;
using SE.Roleplay.Framework.Core.Permissions;
using SE.Roleplay.Framework.Scripting;
using Xunit;

namespace SE.Roleplay.Framework.Scripting.Tests;

public sealed class LuaScriptHostTests
{
    [Fact]
    public void Load_RegistersCommandAndEnforcesPermissionBeforeCallingLua()
    {
        var permissions = new PermissionService();
        var replies = new List<string>();
        using var host = new LuaScriptHost(permissions, (_, message) => replies.Add(message), _ => { });
        var loaded = host.Load("commands.lua",
            "rp.registerCommand('greet', 'rp.greet', function(player, args) return 'Hello ' .. player.name .. ' ' .. args[1] end)",
            "Test Server");

        Assert.True(loaded.Succeeded, loaded.Error);
        Assert.False(host.DispatchCommand(76561198000000001UL, "Alex", "greet", new[] { "there" }));
        Assert.Contains("do not have permission", replies[0]);

        permissions.Grant(76561198000000001UL, "rp.greet");
        Assert.True(host.DispatchCommand(76561198000000001UL, "Alex", "/greet", new[] { "there" }));
        Assert.Contains("Hello Alex there", replies[1]);
    }

    [Fact]
    public void Load_RegistersPlayerLifecycleEvents()
    {
        var logs = new List<string>();
        using var host = new LuaScriptHost(new PermissionService(), (_, _) => { }, logs.Add);
        var loaded = host.Load("events.lua",
            "rp.on('playerJoined', function(player) rp.log('joined:' .. player.name) end); " +
            "rp.on('playerLeft', function(player) rp.log('left:' .. player.name) end)",
            "Test Server");

        Assert.True(loaded.Succeeded, loaded.Error);
        host.PublishPlayerJoined(76561198000000001UL, "Alex");
        host.PublishPlayerLeft(76561198000000001UL, "Alex");
        Assert.Contains(logs, message => message.Contains("joined:Alex"));
        Assert.Contains(logs, message => message.Contains("left:Alex"));
    }

    [Fact]
    public void Load_RejectsDuplicateCommandsAcrossModules()
    {
        using var host = new LuaScriptHost(new PermissionService(), (_, _) => { }, _ => { });
        var first = host.Load("first.lua",
            "rp.registerCommand('status', 'rp.status', function(player, args) return 'ok' end)", "Test");
        var second = host.Load("second.lua",
            "rp.registerCommand('status', 'rp.status', function(player, args) return 'duplicate' end)", "Test");

        Assert.True(first.Succeeded, first.Error);
        Assert.False(second.Succeeded);
        Assert.Contains("already registered", second.Error);
    }

    [Fact]
    public void Load_RejectsUnsupportedPlayerEvents()
    {
        using var host = new LuaScriptHost(new PermissionService(), (_, _) => { }, _ => { });
        var result = host.Load("bad-event.lua",
            "rp.on('serverShutdown', function() end)", "Test");

        Assert.False(result.Succeeded);
        Assert.Contains("Supported events", result.Error);
    }

    [Fact]
    public void Load_StopsScriptsThatExceedInstructionBudget()
    {
        using var host = new LuaScriptHost(new PermissionService(), (_, _) => { }, _ => { }, maximumInstructions: 500);
        var result = host.Load("loop.lua", "while true do end", "Test");

        Assert.False(result.Succeeded);
        Assert.Contains("instruction limit exceeded", result.Error);
    }
}