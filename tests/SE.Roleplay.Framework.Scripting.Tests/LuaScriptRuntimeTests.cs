using System.Collections.Generic;
using SE.Roleplay.Framework.Scripting;
using Xunit;

namespace SE.Roleplay.Framework.Scripting.Tests;

public sealed class LuaScriptRuntimeTests
{
    [Fact] public void Execute_ReturnsScriptResult()
    {
        var result = new LuaScriptRuntime().Execute("welcome.lua", "return 'Welcome to the server'");
        Assert.True(result.Succeeded); Assert.Equal("welcome.lua", result.ScriptName);
        Assert.Contains("Welcome to the server", result.Output); Assert.Null(result.Error);
    }
    [Fact] public void Execute_ReportsLuaSyntaxErrors()
    {
        var result = new LuaScriptRuntime().Execute("broken.lua", "local = ");
        Assert.False(result.Succeeded); Assert.StartsWith("Lua syntax error:", result.Error);
    }
    [Fact] public void Execute_RejectsSourceOverConfiguredLimit()
    {
        var runtime = new LuaScriptRuntime(maximumSourceLength: 4);
        Assert.Throws<System.ArgumentException>(() => runtime.Execute("large.lua", "return 1"));
    }
    [Fact] public void Execute_UsesFreshEnvironmentForEachChunk()
    {
        var runtime = new LuaScriptRuntime(); runtime.Execute("first.lua", "shared_value = 'secret'");
        var second = runtime.Execute("second.lua", "return shared_value");
        Assert.True(second.Succeeded); Assert.NotEqual("secret", second.Output);
    }
    [Fact] public void Execute_ExposesOnlyExplicitRoleplayHostCapabilities()
    {
        var messages = new List<string>(); var context = new LuaScriptContext("Test Community", messages.Add);
        var result = new LuaScriptRuntime().Execute("welcome.lua", "rp.log('player joined'); return rp.serverName", context);
        Assert.True(result.Succeeded); Assert.Contains("Test Community", result.Output);
        Assert.Equal(new[] { "player joined" }, messages);
    }
    [Fact] public void Execute_DoesNotExposeHostApiWithoutContext()
    {
        var result = new LuaScriptRuntime().Execute("no-host.lua", "return rp.serverName");
        Assert.False(result.Succeeded); Assert.StartsWith("Lua runtime error:", result.Error);
    }
    [Fact] public void Execute_TruncatesOversizedLogMessages()
    {
        string? logged = null; var context = new LuaScriptContext("Test Community", value => logged = value);
        var result = new LuaScriptRuntime().Execute("long-log.lua", "rp.log(string.rep('x', 3000))", context);
        Assert.True(result.Succeeded); Assert.Equal(2048, logged?.Length);
    }
}