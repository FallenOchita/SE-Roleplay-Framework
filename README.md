# SE Roleplay Framework

A server-oriented roleplay framework for **Space Engineers 1**, designed to provide a foundation for persistent, community-run RP servers.

> **Current status:** Host-independent core and JSON player persistence are in development. The repository does not yet contain a loadable Torch plugin or an in-game launcher.

## Implemented core

- Host-neutral .NET Standard 2.0 core library
- Player session identity registry
- Typed event bus and module lifecycle manager
- Permission grant/check service
- Persistent JSON player profiles, one file per platform account
- Multiple roleplay characters per account
- Character creation, selection, deletion, and configurable account character limit
- Automatic profile load on session connect and save/unload on disconnect
- Automated tests for profile persistence and character lifecycle
- GitHub Actions build/test workflow

## Player data

The JSON store writes player profiles under the configured data directory:

```
<server-data-directory>/
  Players/
    76561198000000000.json
    76561198000000001.json
```

Each profile contains the platform account ID, last known display name, selected character ID, profile metadata, permission-name collection, and character records. Each character has a stable GUID, name, description, timestamps, and metadata.

Use `PlayerSessionService.ConnectAsync` when the server host reports a player joining. It registers the session and loads the saved profile before publishing `PlayerConnected`. Use `DisconnectAsync` when the host reports a player leaving; it saves and unloads the profile before removing the session.

## Project layout

```
src/
  SE.Roleplay.Framework.Core/
    Abstractions/
    Events/
    Identity/
    Permissions/
    Persistence/
tests/
  SE.Roleplay.Framework.Core.Tests/
docs/
  CORE_ARCHITECTURE.md
```

## Build and test

With the .NET 8 SDK installed:

```sh
dotnet build src/SE.Roleplay.Framework.Core/SE.Roleplay.Framework.Core.csproj --configuration Release
dotnet test tests/SE.Roleplay.Framework.Core.Tests/SE.Roleplay.Framework.Core.Tests.csproj --configuration Release
```

## Compatibility and integration

The core targets **.NET Standard 2.0** as a host-neutral library. The test project targets .NET 8. This does not claim the core is already loadable by every SE1 server host. A separate adapter must be implemented and tested against the exact server loader and game build before deployment.

The JSON provider uses Newtonsoft.Json and stores data in the server data directory. Keep backups of this directory and do not edit player JSON files while the server is running.

No game binaries are bundled. Do not copy proprietary game assemblies into this repository.

## Next milestones

1. Confirm the intended SE1 server host and build its adapter (Torch is a possible target, not yet assumed).
2. Add autosave scheduling and graceful server-shutdown hooks.
3. Persist and restore permission grants through the player profile service.
4. Add admin commands and a character selection interface.
5. Add modular RP systems: jobs, economy, inventory, organizations, and administration.

## Design rules

- Keep game API types outside the core assembly.
- Treat platform account IDs as stable keys; display names are mutable.
- Keep persistence asynchronous at the boundary.
- Do not perform blocking file or database work on game threads.
- Back up persistent player data before upgrading the framework.
