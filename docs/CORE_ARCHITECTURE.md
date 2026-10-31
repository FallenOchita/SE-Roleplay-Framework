# Core architecture

## Boundary

`SE.Roleplay.Framework.Core` is deliberately host-neutral. It must not reference Space Engineers, Torch, or game-specific runtime types. A future SE1 adapter translates host callbacks and platform player identifiers into core calls.

## Services

- **Identity** tracks connected platform accounts and mutable display names. It is session state, not the durable character database.
- **Profiles** loads player records, caches them while online, and serializes per-account changes. It enforces the configured character limit and owns create/select/delete operations.
- **JSON player store** writes one UTF-8 JSON file per platform account. Writes use a temporary file followed by replacement so an interrupted write is less likely to corrupt the previous profile.
- **Permissions** provides named grants and a wildcard grant. The first implementation is in-memory; restoring and saving grants through profiles is a follow-up task.
- **Events** is a synchronous typed event bus. Keep handlers short; schedule slow work away from the game thread in the host adapter.
- **Modules** are initialized and started in registration order, then stopped in reverse order.
- **Sessions** coordinates connection lifecycle: connect registers the player and loads the profile before publishing `PlayerConnected`; disconnect saves/unloads the profile before removing the identity.

## Player data model

A player account can own multiple `PlayerCharacter` records. Each character has a GUID that remains stable across renames and reconnects. The player profile stores the selected character GUID; the session identity mirrors that selection for quick access to the active character ID.

The profile API returns detached snapshots for reads. Character changes must go through `IPlayerProfileService` so they are serialized and saved. The first version supports create, select, delete, and list; editing character fields and richer character attributes will be added with the character-system milestone.

## Threading

Profile operations are asynchronous and serialized per account. The JSON provider serializes file access within its instance. The session identity registry and permission service are intended for serialized server-thread access and are not internally synchronized. The adapter must marshal game callbacks onto the server's approved execution thread; background persistence must not touch game API objects.

## Failure behavior

A failed profile load prevents the session connect operation from completing and removes the temporary identity registration. A failed disconnect save still removes the in-memory identity in a `finally` block; the host should log the exception and retain backups. A module that fails during initialization or startup causes the current lifecycle operation to stop and clean up modules already reached.
