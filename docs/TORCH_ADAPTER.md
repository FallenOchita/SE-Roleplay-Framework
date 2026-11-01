# Torch server adapter

The SE1 host integration lives in `src/SE.Roleplay.Framework.Torch`. The core project remains host-independent and targets .NET Standard 2.0; the Torch adapter targets .NET Framework 4.8 to match Torch's current project target.

## Lifecycle bridge

- Plugin initialization constructs the core services and a JSON player store under the Torch instance's `Storage/SE-Roleplay-Framework/Players` directory.
- `ITorchSessionManager.SessionStateChanged` attaches multiplayer hooks when a world reaches `Loaded`.
- `IMultiplayerManagerBase.PlayerJoined` calls `PlayerSessionService.ConnectAsync`.
- `IMultiplayerManagerBase.PlayerLeft` waits for an in-flight connect operation, then calls `DisconnectAsync`.
- Autosave runs every five minutes from the Torch plugin update callback.
- Session unload and plugin disposal trigger profile flushes.

The event bridge uses Torch's `IPlayer.SteamId` and `IPlayer.Name` properties. The core only sees an unsigned platform account ID and a display name.

## Build boundary

The adapter references local Torch and game assemblies via `TorchLibPath` and `GameBinariesPath`. These assemblies are intentionally not committed. Build and smoke-test against the exact server installation before deployment.

## Known follow-ups

- Add a configurable Torch settings panel and admin/user commands.
- Restore persisted permission grants into the in-memory permission service.
- Add a dedicated integration test harness for Torch session and player events.
- Verify plugin dependency packaging and Newtonsoft.Json assembly resolution on the target Torch build.


## Directory service heartbeat

The Torch adapter supports the directory API using process environment variables:

- `SERPF_DIRECTORY_URL` — HTTPS base URL of the directory service.
- `SERPF_SERVER_ID` — registered server ID.
- `SERPF_SERVER_API_KEY` — secret per-server API key.
- `SERPF_SERVER_MAX_PLAYERS` — optional advertised capacity (default 32).
- `SERPF_SERVER_VERSION` — optional version label (default SE1).

It sends an immediate heartbeat and then one every 30 seconds while the world is loaded. Player counts are maintained from Torch join/leave events. No API key is stored in the launcher or repository. See the Torch adapter README for setup and the current-session tracking limitation.
