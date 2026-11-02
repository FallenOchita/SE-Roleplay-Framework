# SE1 RP scripting runtime

The framework uses C# for trusted server/game integration and embeds Lua for community gameplay extensions.

## Current runtime

- MoonSharp executes Lua in a fresh Preset_SoftSandbox environment for each chunk.
- Each execution has a configurable source-size limit (64 KiB by default).
- Scripts receive no framework, filesystem, process, network, or Torch objects by default.
- Lua failures are returned as structured results so a host can log them without crashing its server loop.

## Planned framework API

The next integration layer will expose a small allowlisted Lua API for registering roleplay commands, subscribing to framework events, and reading/writing player data through permission-checked C# services. Scripts will never receive direct access to Torch internals or unrestricted .NET objects.

## Example

    local message = "Welcome to the community!"
    return message

This runtime foundation does not yet load scripts automatically from a server folder or register game events; those capabilities will be added through the Torch host adapter after API permissions and lifecycle behavior are covered by tests.
