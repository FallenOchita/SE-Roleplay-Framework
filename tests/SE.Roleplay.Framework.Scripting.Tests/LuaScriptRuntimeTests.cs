using SE.Roleplay.Framework.Scripting;

namespace SE.Roleplay.Framework.Scripting.Tests;

public sealed class LuaScriptRuntimeTests
{
    [Fact]
    public void Execute_ReturnsScriptResult()
    {
        var runtime = new LuaScriptRuntime();

        var result = runtime.Execute("welcome.lua", "return 'Welcome to the server'");

        Assert.True(result.Succeeded);
        Assert.Equal("welcome.lua", result.ScriptName);
        Assert.Contains("Welcome to the server", result.Output);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Execute_ReportsLuaSyntaxErrors()
    {
        var runtime = new LuaScriptRuntime();

        var result = runtime.Execute("broken.lua", "local = ");

        Assert.False(result.Succeeded);
        Assert.StartsWith("Lua syntax error:", result.Error);
    }

    [Fact]
    public void Execute_RejectsSourceOverConfiguredLimit()
    {
        var runtime = new LuaScriptRuntime(maximumSourceLength: 4);

        Assert.Throws<ArgumentException>(() => runtime.Execute("large.lua", "return 1"));
    }

    [Fact]
    public void Execute_UsesFreshEnvironmentForEachChunk()
    {
        var runtime = new LuaScriptRuntime();

        runtime.Execute("first.lua", "shared_value = 'secret'");
        var second = runtime.Execute("second.lua", "return shared_value");

        Assert.True(second.Succeeded);
        Assert.NotEqual("secret", second.Output);
    }
}
