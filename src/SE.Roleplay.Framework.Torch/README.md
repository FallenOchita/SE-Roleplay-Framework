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
- Registers in-game and Torch chat commands for per-server character management.

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
| `!rp character delete 2` | Deletes the character at position 2 in the list. |

Character positions are based on the current list order. Players can only operate on the profile associated with their own Steam account. The profile service enforces the server-configured five-character limit. Commands are registered through Torch's `CommandManager` when the plugin is loaded into a session.

## Build requirements

Torch and Space Engineers ship proprietary runtime assemblies. They are not included in this repository and must be supplied from the server installation you use to test the plugin.

- Windows with the .NET Framework 4.8 targeting pack
- .NET SDK capable of building SDK-style `net48` projects
- Torch installation folder containing `Torch.dll`, `Torch.API.dll`, `Torch.Server.dll`, and `NLog.dll`
- Space Engineers dedicated server `DedicatedServer64` folder containing `VRage.Game.dll` and `VRage.Library.dll`

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
- `CreateCharacterAsync`, `GetCharactersAsync`, and `SelectCharacterAsync` for character management.
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
