# SE1 RP scripting runtime

The framework uses C# for trusted server/game integration and embeds Lua for community gameplay extensions.

## Runtime and host API

- MoonSharp executes each chunk in a fresh `Preset_SoftSandbox` environment.
- Source length is limited to 64 KiB by default (configurable per runtime).
- Execution is capped at 100,000 MoonSharp VM instructions per chunk by default (configurable per runtime). The budget is enforced synchronously through MoonSharp's debugger instruction callback; an over-budget script is stopped and returned as a failed execution result.
- The runtime exposes no host objects unless the host explicitly supplies a `LuaScriptContext`.
- The optional `rp` table currently contains only `rp.serverName` (the server display name supplied by the host) and `rp.log(message)` (host logging callback, capped at 2,048 characters).
- No filesystem, process, network, reflection, Torch, or arbitrary .NET objects are exposed.

Example: `rp.log("Welcome script loaded on " .. rp.serverName)`

The host must create a context with a trusted server name and logger callback. Do not pass callbacks that perform privileged operations without authorization.

## Current scope

This is a host API foundation. It does not yet register RP commands or subscribe to Torch gameplay events. Startup loading is opt-in and is documented in the Torch adapter README. The sandbox preset, source-size limit, and instruction budget are defense-in-depth, not a complete security boundary against hostile scripts. Keep script loading disabled unless administrators explicitly enable it, install only trusted scripts, and use operational isolation for public hosting.