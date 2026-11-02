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
    public void LuaChatBroadcast_RequiresPermission_ValidatesMessage_AndRateLimitsPlayer()
    {
        const ulong playerId = 76561198000000001UL;
        var permissions = new PermissionService();
        var broadcasts = new List<string>();
        using var host = new LuaScriptHost(permissions, (_, _) => { }, _ => { },
            broadcastChat: message => { broadcasts.Add(message); return true; });
        var loaded = host.Load("chat.lua",
            "rp.registerCommand('announce', 'rp.announce', function(player, args) " +
            "if player.chat.broadcast(args[1]) then return 'sent' else return 'blocked' end end)",
            "Test Server");

        Assert.True(loaded.Succeeded, loaded.Error);
        permissions.Grant(playerId, "rp.announce");
        Assert.True(host.DispatchCommand(playerId, "Alex", "announce", new[] { "hello world" }));
        Assert.Empty(broadcasts);

        permissions.Grant(playerId, "rp.chat.broadcast");
        Assert.True(host.DispatchCommand(playerId, "Alex", "announce", new[] { "hello world" }));
        Assert.Equal(new[] { "hello world" }, broadcasts);

        Assert.True(host.DispatchCommand(playerId, "Alex", "announce", new[] { new string('x', 281) }));
        Assert.Equal(1, broadcasts.Count);
        Assert.True(host.DispatchCommand(playerId, "Alex", "announce", new[] { "second message" }));
        Assert.Equal(1, broadcasts.Count);
    }

    [Fact]
    public void LuaChatBroadcast_UsesInvokingPlayersPermissionContext()
    {
        const ulong authorizedPlayerId = 76561198000000001UL;
        const ulong otherPlayerId = 76561198000000002UL;
        var permissions = new PermissionService();
        var broadcasts = new List<string>();
        using var host = new LuaScriptHost(permissions, (_, _) => { }, _ => { },
            broadcastChat: message => { broadcasts.Add(message); return true; });
        var loaded = host.Load("chat.lua",
            "rp.registerCommand('announce', 'rp.announce', function(player, args) " +
            "return tostring(player.chat.broadcast(args[1])) end)",
            "Test Server");

        Assert.True(loaded.Succeeded, loaded.Error);
        permissions.Grant(authorizedPlayerId, "rp.announce");
        permissions.Grant(authorizedPlayerId, "rp.chat.broadcast");
        permissions.Grant(otherPlayerId, "rp.announce");

        Assert.True(host.DispatchCommand(otherPlayerId, "Jordan", "announce", new[] { "not authorized" }));
        Assert.Empty(broadcasts);
        Assert.True(host.DispatchCommand(authorizedPlayerId, "Alex", "announce", new[] { "authorized" }));
        Assert.Equal(new[] { "authorized" }, broadcasts);
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
    public void Load_RegistersCharacterLifecycleEvents()
    {
        var logs = new List<string>();
        using var host = new LuaScriptHost(new PermissionService(), (_, _) => { }, logs.Add);
        var loaded = host.Load("characters.lua",
            "rp.on('characterCreated', function(player, character) rp.log('created:' .. player.name .. ':' .. character.name) end); " +
            "rp.on('characterSelected', function(player, character) rp.log('selected:' .. character.id) end); " +
            "rp.on('characterUpdated', function(player, character) rp.log('updated:' .. character.description) end); " +
            "rp.on('characterDeleted', function(player, character) rp.log('deleted:' .. character.name) end)",
            "Test Server");

        Assert.True(loaded.Succeeded, loaded.Error);
        var id = System.Guid.NewGuid();
        host.PublishCharacterCreated(76561198000000001UL, "Alex", id, "Avery", "A pilot");
        host.PublishCharacterSelected(76561198000000001UL, "Alex", id, "Avery", "A pilot");
        host.PublishCharacterUpdated(76561198000000001UL, "Alex", id, "Avery", "A veteran pilot");
        host.PublishCharacterDeleted(76561198000000001UL, "Alex", id, "Avery", "A veteran pilot");

        Assert.Contains(logs, message => message.Contains("created:Alex:Avery"));
        Assert.Contains(logs, message => message.Contains("selected:" + id.ToString("D")));
        Assert.Contains(logs, message => message.Contains("updated:A veteran pilot"));
        Assert.Contains(logs, message => message.Contains("deleted:Avery"));
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