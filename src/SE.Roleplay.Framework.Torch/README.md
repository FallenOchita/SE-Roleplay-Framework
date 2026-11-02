# SE Roleplay Framework — Torch adapter

This project is the Space Engineers 1 dedicated-server host adapter. It keeps Torch and game API references out of `SE.Roleplay.Framework.Core`.

## What it currently does

- Loads as a Torch plugin using `manifest.xml`.
- Creates the host-neutral framework service graph at plugin initialization.
- Uses the Torch session manager to attach/detach multiplayer lifecycle events.
- Loads a player's persistent JSON profile when Torch reports a player joining.
- Saves and unloads the profile when Torch reports a player leaving.
- Runs a five-minute profile autosave while the plugin is active.
- Flushes outstanding player operations and online profiles during plugin disposal.
- Registers in-game and Torch chat commands for per-server character and permission management.

Player JSON files are stored under:

```
<Torch instance>/
  Storage/
    SE-Roleplay-Framework/
      Players/
        <SteamID>.json
```

## Player character commands

Players can manage their own characters through the server chat:

| Command | Description |
| --- | --- |
| `!rp character list` | Lists characters stored by this server; the selected character is marked with `*`. |
| `!rp character create "Name" "Description"` | Creates a character on this server. Description is optional. |
| `!rp character select 2` | Selects the character at position 2 in the list. |
| `!rp character rename 2 "New Name"` | Renames the character at position 2. |
| `!rp character describe 2 "New description"` | Updates its description. |
| `!rp character meta 2 faction "Independent"` | Sets a metadata field. |
| `!rp character metalist 2` | Lists metadata fields. |
| `!rp character metadel 2 faction` | Removes a metadata field. |
| `!rp character delete 2` | Deletes the character at position 2 in the list. |

Character positions are based on the current list order. Players can only operate on the profile associated with their own Steam account. The profile service enforces the server-configured five-character limit. Commands are registered through Torch's `CommandManager` when the plugin is loaded into a session.

## Lua RP scripting

Lua loading remains disabled by default. To enable trusted administrator-installed scripts, set:

```
SERPF_ENABLE_LUA_SCRIPTS=true
```

Place top-level `.lua` files in:

```
<Torch instance>/Storage/SE-Roleplay-Framework/Scripts/
```

The loader accepts up to 50 files, each no larger than 64 KiB. Each module has a fresh `Preset_SoftSandbox` environment and a 100,000 VM-instruction budget per load, command, and player-event callback. The host does not expose filesystem, process, network, reflection, Torch, or arbitrary .NET APIs.

### Lua host API

- `rp.serverName` — server display name.
- `rp.log(message)` — writes a server log entry (message capped at 2,048 characters).
- `rp.registerCommand(name, permission, function(player, args) ... end)` — registers a player command. The required permission is checked by C# before Lua runs.
- `rp.on("playerJoined", function(player) ... end)` — player connected after profile load.
- `rp.on("playerLeft", function(player) ... end)` — player disconnected.

The player table contains `platformId`, `name`, and a `chat` capability. `player.chat.broadcast(message)` requires `rp.chat.broadcast`. `player.chat.send(channel, message)` accepts `global`, `faction`, or `local` and requires the matching `rp.chat.global`, `rp.chat.faction`, or `rp.chat.local` permission. The Torch adapter delivers global chat as `[RP:GLOBAL]`. Local chat is routed to connected players within 500 metres using authoritative server-side world positions. Faction chat is routed only to connected members of the sender's current Space Engineers faction. Private channel messages use a versioned server-to-client secure-message packet on channel `51348` and are displayed by the matching client mod. Channel messages are limited to 280 characters and rate-limited per player/channel. Lua cannot supply or spoof the sender SteamID. Command arguments are a 1-based Lua array. A module may register up to 20 handlers for each supported player event. Command names are unique across loaded modules.

Example:

```lua
rp.registerCommand("greet", "rp.greet", function(player, args)
    return "Welcome, " .. player.name .. "!"
end)

rp.on("playerJoined", function(player)
    rp.log(player.name .. " joined the server")
end)
```

Players with the `rp.greet` permission can invoke it with `!rp script run greet`. Administrators can grant that permission using `!rp admin permissions grant <SteamID64> rp.greet`. The Torch command adapter sends the Lua return value to the invoking player.

Execution budgets count Lua VM instructions, not time spent in host callbacks. Keep callbacks short. This is defense-in-depth, not a complete hostile-code sandbox; enable loading only for trusted scripts and use operational isolation for public servers.

## Persistent player permissions

Server administrators can manage saved profile permissions with the `!rp admin permissions` command category:

| Command | Action |
| --- | --- |
| `!rp admin permissions list <SteamID64>` | Lists saved permission grants. |
| `!rp admin permissions grant <SteamID64> <permission>` | Grants a permission and saves the profile. |
| `!rp admin permissions revoke <SteamID64> <permission>` | Revokes a permission and saves the profile. |
| `!rp admin permissions check <SteamID64> <permission>` | Checks a saved grant, including the `*` wildcard. |

These commands require Torch's Admin promote level. The target player must be online with a loaded profile; profile operations intentionally do not create or edit offline accounts. Permission names are case-insensitive and may not contain whitespace. Grants are persisted to the player profile and immediately synchronized to the runtime `IPermissionService`; they are restored when the player reconnects and cleared when the player disconnects.

## Build requirements

Torch and Space Engineers ship proprietary runtime assemblies. They are not included in this repository and must be supplied from the server installation you use to test the plugin.

- Windows with the .NET Framework 4.8 targeting pack
- .NET SDK capable of building SDK-style `net48` projects
- Torch installation folder containing `Torch.dll`, `Torch.API.dll`, `Torch.Server.dll`, and `NLog.dll`
- Space Engineers dedicated server `DedicatedServer64` folder containing `VRage.Game.dll`, `VRage.Library.dll`, and `VRage.Math.dll`

Build from the repository root:

```powershell
dotnet build src/SE.Roleplay.Framework.Torch/SE.Roleplay.Framework.Torch.csproj -c Release -p:TorchLibPath="C:\Torch" -p:GameBinariesPath="C:\SpaceEngineersDedicated\DedicatedServer64"
```

Adjust both paths to match your installation. Do not copy proprietary game or Torch assemblies into this repository.

## Installation

1. Build the project against the exact Torch and SE dedicated-server versions used by your server.
2. Copy the generated plugin assembly and its framework dependencies to a plugin folder recognized by your Torch installation.
3. Include the generated `manifest.xml` with the plugin.
4. Add/enable the plugin in Torch and start a test world.
5. Confirm the log reports adapter initialization and profile load/save events.
6. Join the server and test the `!rp character` commands above.

Torch plugin packaging and deployment can differ between Torch releases. Validate this package against your installed Torch build before using it on a live world.

## Compatibility note

The adapter follows Torch's `TorchPluginBase`, `ITorchSessionManager`, `IMultiplayerManagerBase`, and `Torch.Commands` APIs. A successful compile against local runtime assemblies and an actual dedicated-server smoke test are still required before calling this adapter production-ready.

## Pre-admission allowlist enforcement (SE1)

The adapter integrates with Torch's dedicated-server `ValidateAuthTicketEvent`. This event is raised during the authentication/connection attempt, before Torch accepts the player. The plugin preserves Torch's existing verdict (bans, Steam authentication, capacity, and group checks) and only performs the directory lookup when that verdict is `OK`.

Enable the gate by setting these environment variables in the Torch server process:

| Variable | Required | Description |
| --- | --- | --- |
| `SERPF_ADMISSION_ENFORCEMENT` | Yes | Set to `true` to register the pre-admission hook. |
| `SERPF_DIRECTORY_URL` | Yes | Directory HTTPS base URL, for example `https://directory.example.net`. |
| `SERPF_SERVER_ID` | Yes | Registered server ID. |
| `SERPF_SERVER_API_KEY` | Yes | Server API key; keep it on the server only. |

The hook calls `GET /api/servers/{serverId}/admission/{steamId64}` with `X-Server-Api-Key`. SteamID64 comes from Torch's server-side authentication event, never from player-supplied data. If the directory says the player is not allowed, Torch rejects the pending connection. The plugin first awaits Torch's existing verdict, so its normal authentication and server checks remain in force.

**Fail-closed behavior:** when enforcement is enabled, a timeout, directory error, invalid response, or incomplete configuration rejects the pending connection. Keep the directory service reachable before enabling enforcement. If `SERPF_ADMISSION_ENFORCEMENT` is not `true`, the admission handler is not registered and directory allowlist settings do not affect joins.

This feature depends on the directory admission endpoints and schema introduced by the companion server-admission-policy change. The plugin requires `Torch.Server.dll` from the same Torch installation used to build and run the server. Build against that exact Torch version and test on a staging world before production use.

## Server-scoped character profiles

Character ownership and persistence belong to the server, not the desktop launcher. Each Torch instance constructs its own `PlayerIdentityService`, `PlayerProfileService`, and `JsonPlayerDataStore`. The store is rooted at that instance's `Storage/SE-Roleplay-Framework/Players` directory, so the same SteamID joining two different server instances receives two independent profile files and character lists.

The server-side profile API exposed through `IFrameworkContext.Profiles` provides:

- `LoadAsync` when a player joins (performed by the Torch adapter).
- `CreateCharacterAsync`, `GetCharactersAsync`, `SelectCharacterAsync`, and `UpdateCharacterAsync` for character management.
- `DeleteCharacterAsync` for character deletion.
- Automatic save/unload on disconnect and periodic server autosave.

The five-character limit is configured by `FrameworkOptions.MaximumCharactersPerPlayer` in the Torch adapter. Each server owner can set this independently.


## Automatic server directory heartbeats

The Torch adapter can publish live player counts to the self-hosted directory service. Configure these environment variables in the Torch server process before starting it:

| Variable | Required | Description |
| --- | --- | --- |
| `SERPF_DIRECTORY_URL` | Yes | Base HTTPS URL of the directory service, for example `https://directory.example.net`. |
| `SERPF_SERVER_ID` | Yes | Server ID returned when the server was registered with the directory API. |
| `SERPF_SERVER_API_KEY` | Yes | Per-server API key returned at registration. Keep it private. |
| `SERPF_SERVER_MAX_PLAYERS` | No | Advertised capacity; defaults to `32`. Valid range: 1–10000. |
| `SERPF_SERVER_VERSION` | No | Version string displayed in the directory; defaults to `SE1`. |

When the world is loaded, the plugin sends an immediate authenticated heartbeat and repeats every 30 seconds. It tracks players through Torch's existing `PlayerJoined` and `PlayerLeft` events. Heartbeats stop when the world unloads or the plugin is disposed. The directory marks the listing Offline after 90 seconds without a heartbeat.

For safety, the adapter requires HTTPS and never logs the API key. Set the variables in the environment of the Torch process, not in a public launcher configuration or a committed file.

**Player-count limitation:** the adapter counts join/leave events observed after it attaches to the session. If the plugin is loaded or reloaded while players are already connected, that initial count may be temporarily lower until those players reconnect. Verify the count against your exact Torch build during server smoke testing.

## Server allowlist administration

Allowlist management is a server feature, not a launcher feature. Configure the same server-only environment values used by admission enforcement:

- `SERPF_DIRECTORY_URL` — HTTPS directory base URL.
- `SERPF_SERVER_ID` — this registered server's ID.
- `SERPF_SERVER_API_KEY` — this server's private API key.

The Torch adapter exposes admin-only allowlist commands in both the Torch server console and in-game admin chat. In-game, run `!rp admin allowlist menu` to display the command menu.

| Command | Action |
| --- | --- |
| `!rp admin allowlist menu` | Displays status and available actions in chat. |
| `!rp admin allowlist status` | Shows enforcement state and member count. |
| `!rp admin allowlist list` | Lists SteamID64 members. |
| `!rp admin allowlist add <SteamID64> [note]` | Adds a member; quote notes containing spaces. |
| `!rp admin allowlist remove <SteamID64>` | Removes a member. |
| `!rp admin allowlist enable` | Enables pre-admission allowlist enforcement. |
| `!rp admin allowlist disable` | Disables enforcement but keeps the list. |

Commands require Torch's Admin promote level. All operations are scoped to the server identified by the server API key. The key stays in the Torch process and is never placed in launcher settings. Admission remains enforced at Torch's pre-connection authentication hook.


## Graphical in-game allowlist panel (initial implementation)

The client mod project in `src/SE.Roleplay.Framework.Client` provides an in-game panel opened with **F6** for players recognized locally as server admins. It includes clickable refresh, add member, remove member, and enforcement-toggle actions.

The Torch adapter registers the matching secure multiplayer message handler while a world session is loaded. For every request it uses the SteamID supplied by Space Engineers' secure-message callback, checks the sender's current server promote level, applies a per-sender request throttle, validates the packet, and then routes only fixed operations to the existing server-scoped directory API. The sender SteamID inside the serialized request is never used for authorization. The directory API key remains in the Torch process.

Build the client mod against the same SE1 installation used by the server:

```powershell
dotnet build src/SE.Roleplay.Framework.Client/SE.Roleplay.Framework.Client.csproj -c Release -p:GameBinariesPath="C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64"
```

Package the resulting client assembly under the mod's `Data/Scripts/SE.Roleplay.Framework.Client/` directory. Install the matching mod on the server and all clients. The channel ID and protocol version must match between client and Torch adapter.

**Validation status:** the secure-message API and session lifecycle are wired in source, but this initial implementation has not yet been built against the target installed SE1/Torch assemblies or tested on a staging server. The client mod packaging, GUI behavior, and server message round-trip must be verified before release. Torch console commands remain available as a fallback.


## Lua startup scripts

The Torch adapter can load trusted Lua startup scripts from `Storage/SE-Roleplay-Framework/Scripts` inside the Torch instance. Script loading is **disabled by default**; set `SERPF_ENABLE_LUA_SCRIPTS=true` in the server process environment to opt in. On plugin initialization it scans only the top-level `*.lua` files, in filename order, with a maximum of 50 files and 64 KiB per file. Scripts run in the MoonSharp restricted sandbox and receive only `rp.serverName` and `rp.log(message)` through the explicit host context. Script output and failures are written to the Torch log.

Only install scripts from sources you trust. The current runtime does not yet enforce a per-script instruction/time budget or provide automatic gameplay event/command hooks; a non-terminating script can consume a worker thread. Keep this feature disabled for untrusted community uploads until execution budgeting and server-side script permissions are implemented.


## Lua chat moderation hook (host API only)

The scripting runtime now supports `rp.on("chatMessage", function(player, message, channel) ... end)`. A handler may return `false` to reject a message; `nil` or `true` allows it. Handlers cannot rewrite text or choose recipients. The C# host exposes `EvaluateChatMessage` for invocation before delivery, validates the trusted sender and channel, and enforces the Lua instruction budget.

**Important:** the current Torch adapter does not yet connect this API to the native Space Engineers player-chat interception pipeline. Native chat moderation is therefore not active yet. A follow-up Torch-specific integration must verify a supported pre-delivery event in the exact installed Torch/SE1 assemblies before this can be enabled.
