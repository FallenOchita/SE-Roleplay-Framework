# Core architecture

## Boundary

`SE.Roleplay.Framework.Core` is deliberately host-neutral. It must not reference Space Engineers, Torch, or game-specific runtime types. A future SE1 adapter translates host callbacks and platform player identifiers into core calls.

## Services

- **Identity** tracks connected platform accounts and mutable display names. It is session state, not the durable character database.
- **Permissions** provides named grants and a wildcard grant. The first implementation is in-memory; the host must load/save grants through player records.
- **Events** is a synchronous typed event bus. Keep handlers short; schedule slow work away from the game thread in the host adapter.
- **Persistence** defines asynchronous player-record storage. A concrete provider will be added after the host's data-directory and serialization requirements are confirmed.
- **Modules** are initialized and started in registration order, then stopped in reverse order.

## Threading

The initial service implementations are intended for serialized server-thread access. They are not yet internally synchronized. The adapter must marshal game callbacks onto the server's approved execution thread; background persistence must not touch game API objects.

## Failure behavior

A module that fails during initialization or startup causes the current lifecycle operation to stop and clean up modules already reached. Shutdown continues even if an individual module's `Stop` method throws; failures are reported through the framework logger.
