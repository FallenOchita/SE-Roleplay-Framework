# SE Roleplay Framework

A server-oriented roleplay framework for **Space Engineers 1**, designed to provide a foundation for persistent, community-run RP servers and a FiveM-style launcher/server ecosystem.

## Current architecture

- The WPF desktop launcher handles Steam sign-in, server discovery, favorites, and joining.
- Each Torch server remains authoritative for its own players, characters, permissions, and gameplay.
- Character profiles are stored per server under the Torch instance's `Storage/SE-Roleplay-Framework/Players` directory. The same Steam account can have different characters on different servers.
- The host-neutral .NET Standard 2.0 core contains session identity, events, module lifecycle, permissions, profile persistence, and character lifecycle services.
- The .NET Framework 4.8 Torch adapter connects the core to Torch session and player lifecycle events.
- The .NET 8 directory API publishes server listings, accepts authenticated server heartbeats, and supports Steam-authenticated server-owner management.

## Repository layout

```
src/
  SE.Roleplay.Framework.Core/       # Host-neutral services and persistence
  SE.Roleplay.Framework.Torch/      # Space Engineers 1 Torch adapter
services/
  SE.Roleplay.Framework.Directory/  # Self-hosted server directory API
launcher/
  SE.Roleplay.Framework.Launcher/   # Windows WPF launcher
tests/
  SE.Roleplay.Framework.Core.Tests/
docs/
  CORE_ARCHITECTURE.md
  TORCH_ADAPTER.md
  DIRECTORY_OWNER_AUTH.md
```

## Launcher

The Windows WPF launcher lives in `launcher/SE.Roleplay.Framework.Launcher/`.

- Steam sign-in uses Steam OpenID in the system browser and verifies the returned assertion. The launcher never asks for or stores a Steam password.
- Play starts Space Engineers through Steam (App ID `244850`); server Join buttons use Steam's `steam://connect/` link.
- Home and Browse Servers show community listings from a configurable HTTPS JSON directory.
- Browse supports search, online/offline/favorites filters, server details, and locally saved favorites.
- Character data is deliberately not stored in the launcher. Secure remote character management requires authenticated server APIs and server-side authorization.

Configure the community directory in the launcher's Settings page. The endpoint must return a JSON document shaped like:

```json
{
  "servers": [
    {
      "id": "community-server-id",
      "name": "Example RP Community",
      "description": "A persistent Space Engineers roleplay server.",
      "address": "play.example.net",
      "port": 27016,
      "players": 0,
      "maxPlayers": 32,
      "region": "North America",
      "status": "Online",
      "version": "Current SE1 version",
      "websiteUrl": "https://example.net",
      "discordUrl": "https://discord.gg/example",
      "rulesUrl": "https://example.net/rules",
      "tags": ["roleplay", "survival"]
    }
  ]
}
```

## Directory API

The self-hosted .NET 8 service in `services/SE.Roleplay.Framework.Directory/` provides:

- `GET /health` — service health check.
- `GET /api/servers` — public server feed compatible with the launcher.
- `POST /api/admin/servers` — bootstrap-admin registration; returns a random per-server API key once.
- `PUT /api/servers/{id}/heartbeat` — authenticated live player-count/version update.
- `DELETE /api/admin/servers/{id}` — bootstrap-admin listing removal.
- Steam OpenID login and callback endpoints for verified owner sessions.
- Authenticated owner endpoints for listing, registering, updating, deleting, and rotating keys for only the signed-in owner's servers.
- CSRF- and exact-Origin-protected owner mutations, plus POST logout.

The Torch adapter can send authenticated HTTPS heartbeats immediately when a world loads and every 30 seconds thereafter. It tracks players from Torch join/leave events. Listings become Offline after 90 seconds without a heartbeat.

Configure the directory service with `SERPF_DIRECTORY_ADMIN_KEY` and optionally `SERPF_DIRECTORY_DATA_PATH`. Configure each Torch process with `SERPF_DIRECTORY_URL`, `SERPF_SERVER_ID`, and `SERPF_SERVER_API_KEY`; optional settings are `SERPF_SERVER_MAX_PLAYERS` and `SERPF_SERVER_VERSION`.

See [the directory service setup guide](services/SE.Roleplay.Framework.Directory/README.md), [Torch heartbeat configuration](src/SE.Roleplay.Framework.Torch/README.md), and [Directory server-owner authentication](docs/DIRECTORY_OWNER_AUTH.md). Host the directory behind HTTPS, keep all keys private, and never put the bootstrap admin key in the launcher or source control.

### Server-owner management status

The server-side Steam owner flow and owner-scoped API are implemented. Steam OpenID assertions are confirmed with Steam; login state is short-lived, browser-bound, and single-use; owner sessions use protected cookies; and state-changing owner requests require the configured HTTPS Origin plus an antiforgery token. Owner authorization is performed inside serialized store operations. Legacy bootstrap-admin listings remain ownerless and are not automatically claimed.

Integration tests cover sign-in callback validation, cookie authentication and logout, owner isolation, CSRF rejection, listing lifecycle, and API-key rotation. See [the owner-authentication design and implementation notes](docs/DIRECTORY_OWNER_AUTH.md).

**Next milestone:** add a launcher-facing owner-management experience that can use the verified server session without exposing the bootstrap admin key or weakening the browser-session, CSRF, and ownership boundaries. The current launcher does not yet provide this UI.

## Build and test

With the .NET 8 SDK installed:

```sh
dotnet build src/SE.Roleplay.Framework.Core/SE.Roleplay.Framework.Core.csproj --configuration Release
dotnet test tests/SE.Roleplay.Framework.Core.Tests/SE.Roleplay.Framework.Core.Tests.csproj --configuration Release
dotnet build services/SE.Roleplay.Framework.Directory/SE.Roleplay.Framework.Directory.csproj --configuration Release
```

The Torch adapter targets .NET Framework 4.8 and requires proprietary Torch and Space Engineers assemblies from the local server installation. Build on Windows with the .NET Framework 4.8 targeting pack and local paths:

```powershell
dotnet build src/SE.Roleplay.Framework.Torch/SE.Roleplay.Framework.Torch.csproj -c Release -p:TorchLibPath="C:\Torch" -p:GameBinariesPath="C:\SpaceEngineersDedicated\DedicatedServer64"
```

The hosted CI validates the host-neutral core/tests and directory API. It cannot compile the Torch adapter without the local proprietary assemblies. Build and smoke-test that adapter against the exact Torch and SE1 server versions before production use.

## Persistence and operational notes

- Each server owns its own player profile data; back up `Storage/SE-Roleplay-Framework` before upgrades.
- Directory API keys are generated cryptographically, stored as SHA-256 hashes, and checked using fixed-time comparison.
- The public directory feed never exposes owner SteamIDs or API-key hashes.
- The JSON directory store uses a process-local lock and atomic file replacement. Run one service instance per data file; use a shared transactional database before horizontal scaling.
- Login-state and Steam nonce replay caches are process-local. Multi-instance deployments need shared atomic storage for these security controls as well.
- Torch player counts are based on join/leave events observed after the plugin attaches. If the plugin loads while players are already connected, the initial count can be temporarily low until those players reconnect.
- Permission grants are not yet restored from player profiles; permission persistence remains a follow-up.
- Do not copy proprietary game or Torch assemblies into this repository.
