using System;
using MoonSharp.Interpreter;

namespace SE.Roleplay.Framework.Scripting
{
    /// <summary>Executes isolated Lua chunks using MoonSharp's restricted sandbox preset.</summary>
    public sealed class LuaScriptRuntime
    {
        public const int DefaultMaximumSourceLength = 64 * 1024;
        private readonly int _maximumSourceLength;

        public LuaScriptRuntime(int maximumSourceLength = DefaultMaximumSourceLength)
        {
            if (maximumSourceLength < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumSourceLength));
            _maximumSourceLength = maximumSourceLength;
        }

        /// <summary>Runs one Lua source chunk in a fresh sandboxed environment.</summary>
        public LuaExecutionResult Execute(string scriptName, string source)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
                throw new ArgumentException("A script name is required.", nameof(scriptName));
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.Length > _maximumSourceLength)
                throw new ArgumentException("The Lua script exceeds the configured source limit.", nameof(source));

            try
            {
                var script = new Script(CoreModules.Preset_SoftSandbox);
                var result = script.DoString(source);
                return LuaExecutionResult.Success(scriptName, result.ToPrintString());
            }
            catch (SyntaxErrorException exception)
            {
                return LuaExecutionResult.Failure(scriptName, "Lua syntax error: " + exception.DecoratedMessage);
            }
            catch (ScriptRuntimeException exception)
            {
                return LuaExecutionResult.Failure(scriptName, "Lua runtime error: " + exception.DecoratedMessage);
            }
            catch (Exception exception)
            {
                return LuaExecutionResult.Failure(scriptName, "Lua execution failed: " + exception.Message);
            }
        }
    }

    /// <summary>Result of a single Lua script execution.</summary>
    public sealed class LuaExecutionResult
    {
        private LuaExecutionResult(string scriptName, bool succeeded, string output, string? error)
        {
            ScriptName = scriptName;
            Succeeded = succeeded;
            Output = output;
            Error = error;
        }

        public string ScriptName { get; }
        public bool Succeeded { get; }
        public string Output { get; }
        public string? Error { get; }

        internal static LuaExecutionResult Success(string name, string output) =>
            new LuaExecutionResult(name, true, output ?? string.Empty, null);

        internal static LuaExecutionResult Failure(string name, string error) =>
            new LuaExecutionResult(name, false, string.Empty, error);
    }
}
