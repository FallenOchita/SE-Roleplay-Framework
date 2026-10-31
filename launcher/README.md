# SE1 Roleplay Framework Launcher

Windows WPF launcher for the Space Engineers 1 Roleplay Framework. This is an early 0.2.0 milestone: it provides a local launcher, settings, automatic Steam install detection, and a configurable community server-directory browser.

## Requirements
- Windows 10/11 x64
- .NET 8 SDK with Windows Desktop targeting support
- Steam and Space Engineers to test game launching
- Inno Setup 6 only when creating the installer

## Build and run
```powershell
dotnet build .\SE.Roleplay.Framework.Launcher\SE.Roleplay.Framework.Launcher.csproj -c Release
dotnet run --project .\SE.Roleplay.Framework.Launcher\SE.Roleplay.Framework.Launcher.csproj
```

## Build the self-contained installer
Install .NET 8 SDK and Inno Setup 6, then run:
```powershell
.\installer\build-installer.ps1
```
Output: `artifacts/SE1-RP-Launcher-Setup-0.2.0-x64.exe`.

A GitHub Actions workflow at `.github/workflows/launcher-installer.yml` builds the installer on Windows when manually dispatched or when a `launcher-v*` tag is pushed.

## Server directory contract
Set the directory URL in Settings. Remote endpoints must use HTTPS; HTTP is allowed only for loopback development. The endpoint returns:
```json
{
  "servers": [{
    "id": "community-id",
    "name": "Community name",
    "description": "Server description",
    "address": "game.example.com",
    "port": 27016,
    "players": 12,
    "maxPlayers": 40,
    "region": "North America",
    "status": "Listed",
    "tags": ["Roleplay", "Survival"]
  }]
}
```
See `examples/server-directory.example.json`.

## Current limitations
- No account registration/authentication or cloud profile sync.
- Character management is a placeholder.
- Server entries are directory metadata, not live heartbeat checks.
- One-click server joining is intentionally not enabled until the SE1 join handoff is verified.
- No modpack installation, launcher auto-update, or signed updates yet.

The launcher is independent of Torch and does not bundle proprietary Space Engineers or Torch assemblies.
