# SE Roleplay Framework

A server-oriented roleplay framework for **Space Engineers 1**, designed to provide a foundation for persistent, community-run RP servers.

> **Current status:** The host-neutral core and JSON player persistence are implemented in the open PR #2. A first Torch server adapter is now under development in a separate project. The adapter still requires compilation against a local Torch/SE installation and a dedicated-server smoke test before deployment.

## Implemented core

- Host-neutral .NET Standard 2.0 core library
- Player session identity registry
- Typed event bus and module lifecycle manager
- Permission grant/check service
- Persistent JSON player profiles, one file per Steam account
- Multiple roleplay characters per account
- Character creation, selection, deletion, and configurable account character limit
- Automatic profile load on session connect and save/unload on disconnect
- Automated tests for profile persistence and character lifecycle

## Torch server adapter

The first host integration is in `src/SE.Roleplay.Framework.Torch/`.

- Loads through Torch's `manifest.xml` plugin manifest and `TorchPluginBase` lifecycle.
- Attaches to Torch session and multiplayer lifecycle events.
- Loads player profiles when players join and saves/unloads them when they leave.
- Runs a five-minute autosave and flushes profiles on world/plugin shutdown.
- Stores player JSON files under the Torch instance's `Storage/SE-Roleplay-Framework/Players` directory.

See [Torch adapter build and installation notes](src/SE.Roleplay.Framework.Torch/README.md) and [adapter architecture](docs/TORCH_ADAPTER.md).

The adapter uses proprietary Torch and Space Engineers assemblies from your own server installation. They are not included in this repository. It has not yet been compiled or smoke-tested against your local server build, so treat it as an integration milestone rather than a production-ready release.

## Player data

The host-neutral JSON store writes one profile per account. Each profile contains the platform account ID, last known display name, selected character ID, profile metadata, permission-name collection, and character records. Each character has a stable GUID, name, description, timestamps, and metadata.

## Project layout

```
src/
  SE.Roleplay.Framework.Core/       # Host-neutral services and persistence
  SE.Roleplay.Framework.Torch/      # Space Engineers 1 Torch host adapter
tests/
  SE.Roleplay.Framework.Core.Tests/
docs/
  CORE_ARCHITECTURE.md
  TORCH_ADAPTER.md
```

## Build and test the core

With the .NET 8 SDK installed:

```sh
dotnet build src/SE.Roleplay.Framework.Core/SE.Roleplay.Framework.Core.csproj --configuration Release
dotnet test tests/SE.Roleplay.Framework.Core.Tests/SE.Roleplay.Framework.Core.Tests.csproj --configuration Release
```

## Build the Torch adapter

The adapter targets .NET Framework 4.8, matching Torch's current project target. Build on Windows with the .NET Framework 4.8 targeting pack and the local server assemblies:

```powershell
dotnet build src/SE.Roleplay.Framework.Torch/SE.Roleplay.Framework.Torch.csproj -c Release -p:TorchLibPath="C:\Torch" -p:GameBinariesPath="C:\SpaceEngineersDedicated\DedicatedServer64"
```

Change those paths to your actual installation. The Torch directory must contain `Torch.dll`, `Torch.API.dll`, and `NLog.dll`; the dedicated-server directory must contain `VRage.Game.dll` and `VRage.Library.dll`.

## Compatibility and safety

- The core targets .NET Standard 2.0 and contains no game API types.
- The Torch adapter is a separate .NET Framework 4.8 assembly.
- Build and test against the exact Torch and SE server versions you intend to run.
- Do not copy proprietary game or Torch assemblies into this repository.
- Back up the server's `Storage/SE-Roleplay-Framework` directory before upgrading.
- Permission grants are not yet restored from player profiles; permission persistence remains a follow-up.

## Next milestones

1. Compile and smoke-test the Torch adapter against the target server installation.
2. Add configurable Torch settings and player/admin commands.
3. Persist and restore permission grants.
4. Add a character selection interface.
5. Add modular RP systems: jobs, economy, inventory, organizations, and administration.

## Design rules

- Keep game API types outside the core assembly.
- Treat Steam account IDs as stable keys; display names are mutable.
- Keep persistence asynchronous at the boundary.
- Avoid blocking file/database work on the game thread.
- Back up persistent player data before upgrading the framework.
