# SE1 Roleplay Framework Launcher

Windows WPF launcher for the Space Engineers 1 Roleplay Framework. The launcher is currently an early **0.2.0 release candidate**; it is not yet a supported production release.

## Current features

- Windows 10/11 x64 desktop application.
- Starts Space Engineers through Steam (App ID `244850`).
- Steam OpenID sign-in through the system browser; the launcher does not collect Steam passwords.
- Optional connection to a configured HTTPS community directory.
- Server browsing, text search, online/offline/favorites filters, server details, and locally saved favorites.
- Steam `steam://connect/` join handoff and a local list of recently joined servers.
- Directory account profile editing (display name and biography).
- Server-owner tools for registering, editing/managing owned listings, rotating server API keys, and managing per-server allowlist membership and policy.
- Protected refresh-token storage using Windows Data Protection APIs.
- Startup self-update checks against the rolling GitHub development release; downloaded installers are SHA-256 verified before launch.

## Requirements

- Windows 10/11 x64.
- .NET 8 SDK for development builds; the installer publishes a self-contained Windows x64 application.
- Steam and Space Engineers 1 for game launch and server-join testing.
- Inno Setup 6 only when creating the installer locally.

## Build and run

```powershell
dotnet build .\SE.Roleplay.Framework.Launcher\SE.Roleplay.Framework.Launcher.csproj --configuration Release
dotnet run --project .\SE.Roleplay.Framework.Launcher\SE.Roleplay.Framework.Launcher.csproj
```

## Build the self-contained installer

Install the .NET 8 SDK and Inno Setup 6, then run:

```powershell
.\installer\build-installer.ps1
```

Output: `artifacts/SE1-RP-Launcher-Setup-0.2.0-x64.exe`.

The `.github/workflows/launcher-installer.yml` workflow builds the actual installer on Windows for launcher pull requests and manual runs. It uploads the installer, SHA-256 checksum, and update manifest as a 14-day artifact. Each push to `main` that changes `launcher/**` publishes or refreshes the `launcher-dev` prerelease with the latest installer and manifest. Installed launchers check that release on startup, compare the embedded source commit, ask before updating, verify the installer checksum, and run the installer. Versioned `launcher-v*` tags publish regular GitHub Releases.

## Server directory configuration

Set the directory URL in Settings. Remote directory endpoints must use HTTPS; HTTP is allowed only for loopback development. The public feed returns a JSON document shaped like:

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
    "status": "Online",
    "tags": ["Roleplay", "Survival"]
  }]
}
```

See `examples/server-directory.example.json` and the directory service setup guide. Server status and player counts are only as current as the directory's heartbeat updates.

## Release-candidate verification checklist

The Windows CI workflow verifies that the WPF project publishes, Inno Setup compiles a non-empty Windows installer, the output has a Windows executable header, and a SHA-256 checksum is produced. These are build/package checks, not a substitute for running the application on a real Windows desktop.

Before calling this a public release, manually verify:

1. Install and uninstall the generated setup executable on a clean Windows 10/11 x64 machine.
2. Launch the installed application and confirm settings persist after restart.
3. Complete Steam OpenID sign-in and directory authorization against a configured HTTPS directory.
4. Browse a populated directory, search/filter servers, favorite one, and confirm it remains favorited after restart.
5. Launch Space Engineers through Steam and join a test server through the server's Join action.
6. Register a test server as an owner, rotate its API key, and exercise allowlist add/remove and policy controls.
7. Confirm the corresponding Torch server is configured for admission enforcement before enabling allowlist enforcement; directory policy alone does not block joins.

## Known limitations

- Character management is not yet implemented in the desktop launcher; the profile page currently edits the directory account display name and biography.
- Server discovery depends on a separately hosted and configured directory service; this repository does not provide a public community directory instance.
- Server owner allowlist controls configure directory policy. The Torch server must also be deployed and configured with valid directory credentials and `SERPF_ADMISSION_ENFORCEMENT=true` for admission decisions to be enforced.
- Automatic updates currently follow successful development-release builds from pushes to `main` that change launcher files. Updates require an internet connection and user confirmation; pull requests do not publish update builds.
- The launcher does not bundle proprietary Space Engineers or Torch assemblies.
- The Torch adapter and real server admission flow still require testing against the exact local SE1/Torch installation.

See the repository root README and `docs/ALLOWLIST_DEPLOYMENT.md` for server-side setup and security requirements.
