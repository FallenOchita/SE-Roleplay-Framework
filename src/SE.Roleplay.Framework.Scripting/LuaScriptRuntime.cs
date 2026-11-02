using System;
using System.Collections.Generic;
using System.Threading;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;

namespace SE.Roleplay.Framework.Scripting
{
    /// <summary>Executes isolated Lua chunks with a deliberately small host API.</summary>
    public sealed class LuaScriptRuntime
    {
        public const int DefaultMaximumSourceLength = 64 * 1024;
        public const int DefaultMaximumInstructionCount = 100_000;
        private readonly int _maximumSourceLength;
        private readonly int _maximumInstructionCount;

        public LuaScriptRuntime(
            int maximumSourceLength = DefaultMaximumSourceLength,
            int maximumInstructionCount = DefaultMaximumInstructionCount)
        {
            if (maximumSourceLength < 1) throw new ArgumentOutOfRangeException(nameof(maximumSourceLength));
            if (maximumInstructionCount < 1) throw new ArgumentOutOfRangeException(nameof(maximumInstructionCount));
            _maximumSourceLength = maximumSourceLength;
            _maximumInstructionCount = maximumInstructionCount;
        }

        public LuaExecutionResult Execute(string scriptName, string source) => Execute(scriptName, source, null);

        public LuaExecutionResult Execute(string scriptName, string source, LuaScriptContext? context)
        {
            if (string.IsNullOrWhiteSpace(scriptName)) throw new ArgumentException("A script name is required.", nameof(scriptName));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Length > _maximumSourceLength) throw new ArgumentException("The Lua script exceeds the configured source limit.", nameof(source));
            try
            {
                var script = new Script(CoreModules.Preset_SoftSandbox);
                var budget = new InstructionBudgetDebugger(_maximumInstructionCount);
                script.AttachDebugger(budget);
                if (context != null)
                {
                    var rp = new Table(script);
                    rp["serverName"] = context.ServerName;
                    rp["log"] = (Action<string>)context.WriteLog;
                    script.Globals["rp"] = rp;
                }
                var result = script.DoString(source);
                return LuaExecutionResult.Success(scriptName, result.ToPrintString());
            }
            catch (LuaInstructionLimitExceededException exception)
            {
                return LuaExecutionResult.Failure(scriptName, exception.Message);
            }
            catch (SyntaxErrorException exception) { return LuaExecutionResult.Failure(scriptName, "Lua syntax error: " + exception.DecoratedMessage); }
            catch (ScriptRuntimeException exception) { return LuaExecutionResult.Failure(scriptName, "Lua runtime error: " + exception.DecoratedMessage); }
            catch (Exception exception) { return LuaExecutionResult.Failure(scriptName, "Lua execution failed: " + exception.Message); }
        }

        /// <summary>
        /// MoonSharp calls IsPauseRequested once for each VM instruction when a debugger is attached.
        /// Throwing from GetAction stops the VM synchronously instead of abandoning a worker thread.
        /// </summary>
        private sealed class InstructionBudgetDebugger : IDebugger
        {
            private readonly int _maximumInstructions;
            private int _instructionCount;
            private int _budgetExceeded;

            public InstructionBudgetDebugger(int maximumInstructions) => _maximumInstructions = maximumInstructions;

            public DebuggerCaps GetDebuggerCaps() => (DebuggerCaps)0;
            public void SetDebugService(DebugService debugService) { }
            public void SetSourceCode(SourceCode sourceCode) { }
            public void SetByteCode(string[] byteCode) { }

            public bool IsPauseRequested()
            {
                if (Interlocked.Increment(ref _instructionCount) <= _maximumInstructions)
                    return false;

                Interlocked.Exchange(ref _budgetExceeded, 1);
                return true;
            }

            public bool SignalRuntimeException(ScriptRuntimeException exception) => false;

            public DebuggerAction GetAction(int instructionPointer, SourceRef sourceReference)
            {
                if (Volatile.Read(ref _budgetExceeded) != 0)
                    throw new LuaInstructionLimitExceededException(_maximumInstructions);

                return new DebuggerAction { Action = DebuggerAction.ActionType.Run };
            }

            public void SignalExecutionEnded() { }
            public void Update(WatchType watchType, IEnumerable<WatchItem> items) { }
            public List<DynamicExpression> GetWatchItems() => new List<DynamicExpression>();
            public void RefreshBreakpoints(IEnumerable<SourceRef> references) { }
        }

        private sealed class LuaInstructionLimitExceededException : Exception
        {
            public LuaInstructionLimitExceededException(int maximumInstructions)
                : base("Lua instruction limit exceeded (" + maximumInstructions + " VM instructions).") { }
        }
    }

    /// <summary>Explicit capabilities supplied to one script execution.</summary>
    public sealed class LuaScriptContext
    {
        private const int MaximumLogMessageLength = 2048;
        private readonly Action<string> _logger;
        public LuaScriptContext(string serverName, Action<string> logger)
        {
            if (string.IsNullOrWhiteSpace(serverName)) throw new ArgumentException("A server name is required.", nameof(serverName));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ServerName = serverName;
        }
        public string ServerName { get; }
        internal void WriteLog(string message)
        {
            if (message == null) message = string.Empty;
            if (message.Length > MaximumLogMessageLength) message = message.Substring(0, MaximumLogMessageLength);
            _logger(message);
        }
    }

    public sealed class LuaExecutionResult
    {
        private LuaExecutionResult(string scriptName, bool succeeded, string output, string? error)
        { ScriptName = scriptName; Succeeded = succeeded; Output = output; Error = error; }
        public string ScriptName { get; }
        public bool Succeeded { get; }
        public string Output { get; }
        public string? Error { get; }
        internal static LuaExecutionResult Success(string name, string output) => new LuaExecutionResult(name, true, output ?? string.Empty, null);
        internal static LuaExecutionResult Failure(string name, string error) => new LuaExecutionResult(name, false, string.Empty, error);
    }
}