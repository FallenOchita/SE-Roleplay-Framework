# SE Roleplay Framework

A server-oriented roleplay framework for **Space Engineers 1**, designed to provide a foundation for persistent, community-run RP servers.

> **Current status:** Core foundation in development. This repository does not yet contain a loadable Torch plugin or an in-game launcher.

## Core milestone

This first milestone establishes host-independent building blocks:

- Player session identity registry
- Permission grants and permission checks
- Typed event bus with disposable subscriptions
- Player profile persistence contract
- Framework configuration model
- Module lifecycle and service context contracts

The core intentionally has no direct dependency on Space Engineers or Torch assemblies. This keeps business logic testable and lets a later server adapter bridge the framework to the selected SE1 server host.

## Project layout

```
src/
  SE.Roleplay.Framework.Core/
    Abstractions/
    Events/
    Identity/
    Permissions/
    Persistence/
```

## Compatibility and integration

The core targets **.NET Standard 2.0** as a host-neutral library. This is not a claim that it is already loadable by every SE1 server host. A separate adapter must be implemented and tested against the exact server loader and game build before deployment.

No game binaries are bundled. Do not copy proprietary game assemblies into this repository.

## Build

With a .NET SDK installed:

```sh
dotnet build src/SE.Roleplay.Framework.Core/SE.Roleplay.Framework.Core.csproj
```

## Planned next steps

1. Add automated core tests and thread-safety decisions.
2. Implement a durable storage provider and configuration loader.
3. Select and build the SE1 server-host adapter (for example, a Torch integration if that is the chosen host).
4. Add server lifecycle hooks and player connection events.
5. Add modular RP systems: characters, jobs, economy, inventory, organizations, and administration.

## Design rules

- Keep game API types outside the core assembly.
- Treat platform account IDs as stable keys; display names are mutable.
- Keep persistence asynchronous at the boundary.
- Validate permission names at the host command/API boundary.
- Do not perform blocking file or database work on game threads.
