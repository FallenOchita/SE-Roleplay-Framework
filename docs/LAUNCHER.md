# SE Roleplay Launcher

A separate Windows desktop client for discovering and joining Space Engineers roleplay communities. Players use the launcher; server owners can later run the framework's server-side components. A player does not need Torch installed.

## Current implementation
- WPF desktop shell with Server Browser, Direct Connect, and Settings views.
- Optional HTTP(S) JSON server directory.
- Direct connection handoff through Steam's `steam://connect/host:port` URI.
- Local settings at `%LOCALAPPDATA%/SERoleplayLauncher/settings.json`.
- No dedicated server or Torch installation required on the player's PC.

## Server directory contract
The configured URL returns a JSON array:

```json
[{"id":"example-rp","name":"Example Roleplay","description":"A community-run roleplay server.","address":"play.example.net","port":27016,"region":"North America","gameMode":"Serious RP","players":12,"maxPlayers":32,"isOnline":true,"version":"0.1"}]
```

The repository does not yet operate a public directory service, so the browser starts empty until a directory endpoint is configured. Population and online status must come from a real heartbeat service; the launcher does not fabricate live data.

## Build
On Windows with .NET 8 SDK:

```powershell
dotnet build src/SE.Roleplay.Launcher/SE.Roleplay.Launcher.csproj -c Release
dotnet run --project src/SE.Roleplay.Launcher/SE.Roleplay.Launcher.csproj
```

## Roadmap
1. Public server directory API and server-owner registration/heartbeat.
2. Server detail pages, tags, search, filters, favorites, and player counts.
3. Framework package/version manager with safe update and rollback.
4. Steam/game installation detection and launch diagnostics.
5. Account identity and character selection after the server API exists.

The launcher currently hands off connection to Steam. It does not yet install mods, authenticate roleplay accounts, or guarantee that a server is reachable.