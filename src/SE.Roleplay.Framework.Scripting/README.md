# SE1 RP scripting runtime

The framework uses C# for trusted server/game integration and embeds Lua for community gameplay extensions.

## Runtime and host API

- MoonSharp executes each chunk in a fresh `Preset_SoftSandbox` environment.
- Source length is limited to 64 KiB by default (configurable per runtime).
- Execution is capped at 100,000 MoonSharp VM instructions per chunk by default (configurable per runtime). The budget is enforced synchronously through MoonSharp's debugger instruction callback; an over-budget script is stopped and returned as a failed execution result.
- The host exposes only a narrow `rp` API: `serverName`, `log(message)`, `registerCommand(name, permission, handler)`, and `on(eventName, handler)`.
- Commands are registered once during module load and checked against the C# `IPermissionService` before each invocation.
- Player command/event contexts expose `player.chat.broadcast(message)` and `player.chat.send(channel, message)`. Channel sends accept only `global`, `faction`, or `local`; C# independently checks `rp.chat.global`, `rp.chat.faction`, or `rp.chat.local`, validates messages (280 characters maximum, no control characters), and rate-limits each player per channel to one message every three seconds. The trusted host callback receives the actual platform ID; Lua cannot supply a sender identity.
- Chat broadcasts use the fixed `[RP]` server label; scripts cannot choose another player's identity or bypass host permission checks. Channel routing is delegated to a trusted host callback: hosts without verified local-position or faction-membership providers must reject those channels rather than guess recipients.
- Supported events are `playerJoined`, `playerLeft`, `characterCreated`, `characterSelected`, `characterUpdated`, `characterDeleted`, and `chatMessage`.
- `rp.on("chatMessage", function(player, message, channel) ... end)` registers a moderation hook. Return `false` to reject the message; return `nil` or `true` to allow it. Other return values do not rewrite the original message. Each handler receives a trusted player identity snapshot, the original text, and a normalized `global`, `local`, or `faction` channel. Each callback receives the standard VM instruction budget. Handler exceptions are logged and fail open so a broken script does not silently suppress player chat.
- Character event handlers receive the player table plus a read-only snapshot table containing character `id`, `name`, and `description`. Character metadata and account permissions are not exposed.
- No filesystem, process, network, reflection, Torch, or arbitrary .NET objects are exposed.

Example: `rp.log("Welcome script loaded on " .. rp.serverName)`

The host must create a context with a trusted server name and logger callback. Do not pass callbacks that perform privileged operations without authorization.

## Current scope

The Torch adapter loads modules at startup (opt-in), routes player commands through `!rp script run`, forwards player join/leave plus character create/select/update/delete lifecycle events, and sends authorized Lua RP broadcasts through the server chat API. Command permission checks remain in C# and are not delegated to Lua. The sandbox preset, source-size limit, and instruction budget are defense-in-depth, not a complete security boundary against hostile scripts. VM instruction budgets do not bound time spent inside host callbacks. Keep script loading disabled unless administrators explicitly enable it, install only trusted scripts, and use operational isolation for public hosting.

### Chat moderation integration boundary

The scripting host exposes `EvaluateChatMessage(platformId, displayName, channel, message)` for a host adapter to call before delivery. It validates the trusted platform ID, channel, and 280-character message limit. A handler can only allow or deny; it cannot change the sender, recipients, channel, or message text. This host API is covered by automated tests. **The current Torch adapter does not yet attach this method to Space Engineers' native player-chat interception pipeline**, so ordinary game chat is not moderated by Lua until that verified pre-delivery hook is implemented for the target Torch/SE1 version. Do not advertise this as active server-wide moderation yet.
