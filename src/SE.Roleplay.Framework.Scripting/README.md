# SE1 RP scripting runtime

The framework uses C# for trusted server/game integration and embeds Lua for community gameplay extensions.

## Runtime and host API

- MoonSharp executes each chunk in a fresh `Preset_SoftSandbox` environment.
- Source length is limited to 64 KiB by default (configurable per runtime).
- The runtime exposes no host objects unless the host explicitly supplies a `LuaScriptContext`.
- The optional `rp` table currently contains only `rp.serverName` (read-only display name) and `rp.log(message)` (host logging callback, capped at 2,048 characters).
- No filesystem, process, network, reflection, Torch, or arbitrary .NET objects are exposed.

Example: `rp.log("Welcome script loaded on " .. rp.serverName)`

The host must create a context with a trusted server name and logger callback. Do not pass callbacks that perform privileged operations without authorization.

## Current scope

This is a host API foundation. It does not yet load scripts automatically from a server folder, register RP commands, or subscribe to Torch events. Those capabilities will be added through explicit, permission-checked interfaces and lifecycle tests. The sandbox preset and input limits are defense-in-depth, not a complete security boundary against hostile scripts; production hosting still needs execution budgets and operational isolation.