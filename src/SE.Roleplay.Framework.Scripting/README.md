# SE1 RP scripting runtime

The framework uses C# for trusted server/game integration and embeds Lua for community gameplay extensions.

## Runtime and host API

- MoonSharp executes each chunk in a fresh `Preset_SoftSandbox` environment.
- Source length is limited to 64 KiB by default (configurable per runtime).
- Execution is capped at 100,000 MoonSharp VM instructions per chunk by default (configurable per runtime). The budget is enforced synchronously through MoonSharp's debugger instruction callback; an over-budget script is stopped and returned as a failed execution result.
- The host exposes only a narrow `rp` API: `serverName`, `log(message)`, `registerCommand(name, permission, handler)`, and `on(eventName, handler)`.
- Commands are registered once during module load and checked against the C# `IPermissionService` before each invocation.
- Supported events are `playerJoined`, `playerLeft`, `characterCreated`, `characterSelected`, `characterUpdated`, and `characterDeleted`.
- Character event handlers receive the player table plus a read-only snapshot table containing character `id`, `name`, and `description`. Character metadata and account permissions are not exposed.
- No filesystem, process, network, reflection, Torch, or arbitrary .NET objects are exposed.

Example: `rp.log("Welcome script loaded on " .. rp.serverName)`

The host must create a context with a trusted server name and logger callback. Do not pass callbacks that perform privileged operations without authorization.

## Current scope

The Torch adapter loads modules at startup (opt-in), routes player commands through `!rp script run`, and forwards player join/leave plus character create/select/update/delete lifecycle events. Command permission checks remain in C# and are not delegated to Lua. The sandbox preset, source-size limit, and instruction budget are defense-in-depth, not a complete security boundary against hostile scripts. VM instruction budgets do not bound time spent inside host callbacks. Keep script loading disabled unless administrators explicitly enable it, install only trusted scripts, and use operational isolation for public hosting.