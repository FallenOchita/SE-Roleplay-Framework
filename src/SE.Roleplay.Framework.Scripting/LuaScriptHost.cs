using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;
using SE.Roleplay.Framework.Core.Permissions;

namespace SE.Roleplay.Framework.Scripting
{
    /// <summary>
    /// Hosts trusted Lua modules and exposes only explicitly registered RP commands and player lifecycle events.
    /// All command authorization is performed by the C# permission service before Lua is invoked.
    /// </summary>
    public sealed class LuaScriptHost : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, LoadedModule> _modules = new Dictionary<string, LoadedModule>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RegisteredCommand> _commands = new Dictionary<string, RegisteredCommand>(StringComparer.OrdinalIgnoreCase);
        private readonly IPermissionService _permissions;
        private readonly Action<ulong, string> _reply;
        private readonly Action<string> _logger;
        private readonly Func<string, bool>? _broadcastChat;
        private readonly ConcurrentDictionary<ulong, long> _lastChatBroadcastUtcTicks = new ConcurrentDictionary<ulong, long>();
        private readonly int _maximumInstructions;
        private bool _disposed;

        public LuaScriptHost(IPermissionService permissions, Action<ulong, string> reply, Action<string> logger,
            int maximumInstructions = LuaScriptRuntime.DefaultMaximumInstructionCount,
            Func<string, bool>? broadcastChat = null)
        {
            _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
            _reply = reply ?? throw new ArgumentNullException(nameof(reply));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _broadcastChat = broadcastChat;
            if (maximumInstructions < 1) throw new ArgumentOutOfRangeException(nameof(maximumInstructions));
            _maximumInstructions = maximumInstructions;
        }

        /// <summary>Loads one module and atomically publishes its command and event registrations.</summary>
        public LuaModuleLoadResult Load(string scriptName, string source, string serverName)
        {
            if (string.IsNullOrWhiteSpace(scriptName)) throw new ArgumentException("A script name is required.", nameof(scriptName));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Length > LuaScriptRuntime.DefaultMaximumSourceLength)
                return LuaModuleLoadResult.Failure(scriptName, "The Lua script exceeds the 64 KiB source limit.");
            if (string.IsNullOrWhiteSpace(serverName)) throw new ArgumentException("A server name is required.", nameof(serverName));

            lock (_sync)
            {
                ThrowIfDisposed();
                if (_modules.ContainsKey(scriptName))
                    return LuaModuleLoadResult.Failure(scriptName, "A Lua module with this name is already loaded.");

                var module = new LoadedModule(scriptName, new Script(CoreModules.Preset_SoftSandbox), _maximumInstructions);
                RegisterHostApi(module, serverName);
                try
                {
                    module.Script.DoString(source, null, scriptName);
                    foreach (var command in module.PendingCommands)
                    {
                        if (_commands.ContainsKey(command.Name))
                            throw new InvalidOperationException("The RP command '" + command.Name + "' is already registered.");
                    }

                    _modules.Add(scriptName, module);
                    foreach (var command in module.PendingCommands)
                        _commands.Add(command.Name, new RegisteredCommand(module, command));
                    _logger("Loaded Lua module " + scriptName + " with " + module.PendingCommands.Count + " command(s) and " +
                        module.EventHandlers.Count + " event handler(s).");
                    return LuaModuleLoadResult.Success(scriptName);
                }
                catch (Exception exception)
                {
                    module.Dispose();
                    var message = module.BudgetExceeded
                        ? "Lua instruction limit exceeded (" + _maximumInstructions + " VM instructions)."
                        : "Lua module failed: " + exception.Message;
                    return LuaModuleLoadResult.Failure(scriptName, message);
                }
            }
        }

        /// <summary>Dispatches a command only after checking its required permission in the host.</summary>
        public bool DispatchCommand(ulong platformId, string displayName, string commandName, IReadOnlyList<string> arguments) =>
            DispatchCommand(platformId, displayName, commandName, arguments, message => _reply(platformId, message));

        public bool DispatchCommand(ulong platformId, string displayName, string commandName,
            IReadOnlyList<string> arguments, Action<string> reply)
        {
            if (reply == null) throw new ArgumentNullException(nameof(reply));
            if (platformId == 0) return false;
            if (string.IsNullOrWhiteSpace(commandName)) return false;
            RegisteredCommand registration;
            lock (_sync)
            {
                if (_disposed || !_commands.TryGetValue(NormalizeCommand(commandName), out registration))
                    return false;
            }

            if (!_permissions.HasPermission(platformId, registration.Definition.RequiredPermission))
            {
                reply("You do not have permission to use /" + registration.Definition.Name + ".");
                return false;
            }

            var module = registration.Module;
            lock (module.Sync)
            {
                if (module.IsDisposed) return false;
                try
                {
                    module.Budget.Reset();
                    var player = CreatePlayerTable(module.Script, platformId, displayName);
                    var args = CreateArgumentsTable(module.Script, arguments ?? Array.Empty<string>());
                    var result = module.Script.Call(registration.Definition.Handler, DynValue.NewTable(player), DynValue.NewTable(args));
                    if (result.Type != DataType.Nil && result.Type != DataType.Void)
                    {
                        var response = result.ToPrintString();
                        if (!string.IsNullOrWhiteSpace(response))
                            reply(response.Length > 2048 ? response.Substring(0, 2048) : response);
                    }
                    return true;
                }
                catch (Exception exception)
                {
                    _logger("Lua command /" + registration.Definition.Name + " in " + module.Name + " failed: " + exception.Message);
                    reply("That RP command could not be completed.");
                    return false;
                }
            }
        }

        public void PublishPlayerJoined(ulong platformId, string displayName) =>
            PublishPlayerEvent("playerJoined", platformId, displayName);

        public void PublishPlayerLeft(ulong platformId, string displayName)
        {
            _lastChatBroadcastUtcTicks.TryRemove(platformId, out _);
            PublishPlayerEvent("playerLeft", platformId, displayName);
        }

        public void PublishCharacterCreated(ulong platformId, string displayName, Guid characterId, string name, string description) =>
            PublishCharacterEvent("characterCreated", platformId, displayName, characterId, name, description);

        public void PublishCharacterSelected(ulong platformId, string displayName, Guid characterId, string name, string description) =>
            PublishCharacterEvent("characterSelected", platformId, displayName, characterId, name, description);

        public void PublishCharacterUpdated(ulong platformId, string displayName, Guid characterId, string name, string description) =>
            PublishCharacterEvent("characterUpdated", platformId, displayName, characterId, name, description);

        public void PublishCharacterDeleted(ulong platformId, string displayName, Guid characterId, string name, string description) =>
            PublishCharacterEvent("characterDeleted", platformId, displayName, characterId, name, description);

        private void PublishPlayerEvent(string eventName, ulong platformId, string displayName)
        {
            if (platformId == 0) return;
            LoadedModule[] modules;
            lock (_sync)
            {
                if (_disposed) return;
                modules = _modules.Values.ToArray();
            }

            foreach (var module in modules)
            {
                DynValue[] handlers;
                lock (module.Sync)
                {
                    if (module.IsDisposed || !module.EventHandlers.TryGetValue(eventName, out var registered))
                        continue;
                    handlers = registered.ToArray();
                }

                foreach (var handler in handlers)
                {
                    lock (module.Sync)
                    {
                        if (module.IsDisposed) break;
                        try
                        {
                            module.Budget.Reset();
                            module.Script.Call(handler, DynValue.NewTable(CreatePlayerTable(module.Script, platformId, displayName)));
                        }
                        catch (Exception exception)
                        {
                            _logger("Lua " + eventName + " handler in " + module.Name + " failed: " + exception.Message);
                        }
                    }
                }
            }
        }

        private void PublishCharacterEvent(string eventName, ulong platformId, string displayName,
            Guid characterId, string characterName, string description)
        {
            if (platformId == 0 || characterId == Guid.Empty) return;
            LoadedModule[] modules;
            lock (_sync)
            {
                if (_disposed) return;
                modules = _modules.Values.ToArray();
            }

            foreach (var module in modules)
            {
                DynValue[] handlers;
                lock (module.Sync)
                {
                    if (module.IsDisposed || !module.EventHandlers.TryGetValue(eventName, out var registered))
                        continue;
                    handlers = registered.ToArray();
                }

                foreach (var handler in handlers)
                {
                    lock (module.Sync)
                    {
                        if (module.IsDisposed) break;
                        try
                        {
                            module.Budget.Reset();
                            var player = DynValue.NewTable(CreatePlayerTable(module.Script, platformId, displayName));
                            var character = new Table(module.Script);
                            character["id"] = characterId.ToString("D");
                            character["name"] = characterName ?? string.Empty;
                            character["description"] = description ?? string.Empty;
                            module.Script.Call(handler, player, DynValue.NewTable(character));
                        }
                        catch (Exception exception)
                        {
                            _logger("Lua " + eventName + " handler in " + module.Name + " failed: " + exception.Message);
                        }
                    }
                }
            }
        }

        private void RegisterHostApi(LoadedModule module, string serverName)
        {
            var rp = new Table(module.Script);
            rp["serverName"] = serverName;
            rp["log"] = (Action<string>)(message =>
            {
                var safeMessage = message ?? string.Empty;
                if (safeMessage.Length > 2048) safeMessage = safeMessage.Substring(0, 2048);
                _logger("[Lua:" + module.Name + "] " + safeMessage);
            });
            rp["registerCommand"] = (Action<string, string, DynValue>)((name, permission, handler) =>
            {
                var commandName = NormalizeCommand(name);
                if (commandName.Length == 0 || commandName.Length > 32 ||
                    commandName.Any(character => !(char.IsLetterOrDigit(character) || character == '_' || character == '-')))
                    throw new ScriptRuntimeException("Command names must be 1-32 letters, digits, underscores, or hyphens.");
                if (string.IsNullOrWhiteSpace(permission) || permission.Trim().Length > 100 || permission.Any(char.IsWhiteSpace))
                    throw new ScriptRuntimeException("A valid permission name is required for every RP command.");
                if (handler == null || handler.Type != DataType.Function)
                    throw new ScriptRuntimeException("The RP command handler must be a function.");
                if (module.PendingCommands.Any(item => string.Equals(item.Name, commandName, StringComparison.OrdinalIgnoreCase)))
                    throw new ScriptRuntimeException("This module already registered the command '" + commandName + "'.");
                module.PendingCommands.Add(new PendingCommand(commandName, permission.Trim(), handler));
            });
            rp["on"] = (Action<string, DynValue>)((eventName, handler) =>
            {
                var supportedEvents = new[]
                {
                    "playerJoined", "playerLeft", "characterCreated", "characterSelected",
                    "characterUpdated", "characterDeleted"
                };
                var normalizedEvent = supportedEvents.FirstOrDefault(value =>
                    string.Equals(value, eventName, StringComparison.OrdinalIgnoreCase));
                if (normalizedEvent == null)
                    throw new ScriptRuntimeException("Supported events are playerJoined, playerLeft, characterCreated, characterSelected, characterUpdated, and characterDeleted.");
                if (handler == null || handler.Type != DataType.Function)
                    throw new ScriptRuntimeException("The event handler must be a function.");
                var key = normalizedEvent;
                if (!module.EventHandlers.TryGetValue(key, out var handlers))
                    module.EventHandlers[key] = handlers = new List<DynValue>();
                if (handlers.Count >= 20)
                    throw new ScriptRuntimeException("A module may register at most 20 handlers per player event.");
                handlers.Add(handler);
            });
            module.Script.Globals["rp"] = rp;
        }

        private Table CreatePlayerTable(Script script, ulong platformId, string displayName)
        {
            var player = new Table(script);
            player["platformId"] = platformId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            player["name"] = displayName ?? string.Empty;

            // This capability closes over the trusted platform ID. Lua cannot spoof another player's identity.
            var chat = new Table(script);
            chat["broadcast"] = (Func<string, bool>)(message => TryBroadcastChat(platformId, message));
            player["chat"] = chat;
            return player;
        }

        private bool TryBroadcastChat(ulong platformId, string message)
        {
            if (platformId == 0 || _broadcastChat == null ||
                !_permissions.HasPermission(platformId, "rp.chat.broadcast"))
                return false;

            if (string.IsNullOrWhiteSpace(message) || message.Length > 280 ||
                message.Any(char.IsControl))
                return false;

            var safeMessage = message.Trim();
            if (safeMessage.Length == 0 || safeMessage.Length > 280)
                return false;

            const long minimumIntervalTicks = TimeSpan.TicksPerSecond * 3;
            while (true)
            {
                var now = DateTime.UtcNow.Ticks;
                if (_lastChatBroadcastUtcTicks.TryGetValue(platformId, out var previous))
                {
                    if (now - previous < minimumIntervalTicks)
                        return false;
                    if (!_lastChatBroadcastUtcTicks.TryUpdate(platformId, now, previous))
                        continue;
                }
                else if (!_lastChatBroadcastUtcTicks.TryAdd(platformId, now))
                {
                    continue;
                }

                break;
            }

            try
            {
                return _broadcastChat(safeMessage);
            }
            catch (Exception exception)
            {
                _logger("Lua RP chat broadcast failed: " + exception.Message);
                return false;
            }
        }

        private static Table CreateArgumentsTable(Script script, IReadOnlyList<string> arguments)
        {
            var table = new Table(script);
            for (var index = 0; index < arguments.Count && index < 32; index++)
                table[index + 1] = arguments[index] ?? string.Empty;
            return table;
        }

        private static string NormalizeCommand(string name) => (name ?? string.Empty).Trim().TrimStart('/').ToLowerInvariant();

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _commands.Clear();
                foreach (var module in _modules.Values) module.Dispose();
                _modules.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LuaScriptHost));
        }

        private sealed class RegisteredCommand
        {
            public RegisteredCommand(LoadedModule module, PendingCommand definition) { Module = module; Definition = definition; }
            public LoadedModule Module { get; }
            public PendingCommand Definition { get; }
        }

        private sealed class PendingCommand
        {
            public PendingCommand(string name, string requiredPermission, DynValue handler)
            { Name = name; RequiredPermission = requiredPermission; Handler = handler; }
            public string Name { get; }
            public string RequiredPermission { get; }
            public DynValue Handler { get; }
        }

        private sealed class LoadedModule : IDisposable
        {
            public LoadedModule(string name, Script script, int maximumInstructions)
            {
                Name = name;
                Script = script;
                Budget = new InstructionBudgetDebugger(maximumInstructions);
                Script.AttachDebugger(Budget);
            }
            public string Name { get; }
            public Script Script { get; }
            public InstructionBudgetDebugger Budget { get; }
            public object Sync { get; } = new object();
            public List<PendingCommand> PendingCommands { get; } = new List<PendingCommand>();
            public Dictionary<string, List<DynValue>> EventHandlers { get; } = new Dictionary<string, List<DynValue>>(StringComparer.OrdinalIgnoreCase);
            public bool IsDisposed { get; private set; }
            public bool BudgetExceeded => Budget.Exceeded;
            public void Dispose() { lock (Sync) IsDisposed = true; }
        }

        private sealed class InstructionBudgetDebugger : IDebugger
        {
            private readonly int _maximumInstructions;
            private int _instructionCount;
            private int _budgetExceeded;
            public InstructionBudgetDebugger(int maximumInstructions) => _maximumInstructions = maximumInstructions;
            public bool Exceeded => System.Threading.Volatile.Read(ref _budgetExceeded) != 0;
            public void Reset() { System.Threading.Interlocked.Exchange(ref _instructionCount, 0); System.Threading.Interlocked.Exchange(ref _budgetExceeded, 0); }
            public DebuggerCaps GetDebuggerCaps() => (DebuggerCaps)0;
            public void SetDebugService(DebugService debugService) { }
            public void SetSourceCode(SourceCode sourceCode) { }
            public void SetByteCode(string[] byteCode) { }
            public bool IsPauseRequested()
            {
                if (System.Threading.Interlocked.Increment(ref _instructionCount) <= _maximumInstructions) return false;
                System.Threading.Interlocked.Exchange(ref _budgetExceeded, 1);
                return true;
            }
            public bool SignalRuntimeException(ScriptRuntimeException exception) => false;
            public DebuggerAction GetAction(int instructionPointer, SourceRef sourceReference)
            {
                if (Exceeded) throw new ScriptRuntimeException("Lua instruction limit exceeded (" + _maximumInstructions + " VM instructions).");
                return new DebuggerAction { Action = DebuggerAction.ActionType.Run };
            }
            public void SignalExecutionEnded() { }
            public void Update(WatchType watchType, IEnumerable<WatchItem> items) { }
            public List<DynamicExpression> GetWatchItems() => new List<DynamicExpression>();
            public void RefreshBreakpoints(IEnumerable<SourceRef> references) { }
        }
    }

    public sealed class LuaModuleLoadResult
    {
        private LuaModuleLoadResult(string name, bool succeeded, string? error)
        { ScriptName = name; Succeeded = succeeded; Error = error; }
        public string ScriptName { get; }
        public bool Succeeded { get; }
        public string? Error { get; }
        internal static LuaModuleLoadResult Success(string name) => new LuaModuleLoadResult(name, true, null);
        internal static LuaModuleLoadResult Failure(string name, string error) => new LuaModuleLoadResult(name, false, error);
    }
}