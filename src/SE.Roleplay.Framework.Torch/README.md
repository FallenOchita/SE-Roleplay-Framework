# SE Roleplay Framework — Torch adapter

This project is the Space Engineers 1 dedicated-server host adapter. It keeps Torch and game API references out of `SE.Roleplay.Framework.Core`.

## What it currently does

- Loads as a Torch plugin using `manifest.xml`.
- Creates the host-neutral framework service graph at plugin initialization.
- Uses the Torch session manager to attach/detach multiplayer lifecycle events.
- Loads a player's persistent JSON profile when Torch reports a player joining.
- Saves and unloads the profile when Torch reports a player leaving.
- Runs a five-minute profile autosave while the plugin is active.
- Flushes outstanding player operations and online profiles during plugin disposal.
- Registers in-game and Torch chat commands for per-server character management.

Player JSON files are stored under:

```
<Torch instance>/
  Storage/
    SE-Roleplay-Framework/
      Players/
        <SteamID>.json
```

## Player character commands

Players can manage their own characters through the server chat:

| Command | Description |
| --- | --- |
| `!rp character list` | Lists characters stored by this server; the selected character is marked with `*`. |
| `!rp character create "Name" "Description"` | Creates a character on this server. Description is optional. |
| `!rp character select 2` | Selects the character at position 2 in the list. |
| `!rp character delete 2` | Deletes the character at position 2 in the list. |

Character positions are based on the current list order. Players can only operate on the profile associated with their own Steam account. The profile service enforces the server-configured five-character limit. Commands are registered through Torch's `CommandManager` when the plugin is loaded into a session.

## Build requirements

Torch and Space Engineers ship proprietary runtime assemblies. They are not included in this repository and must be supplied from the server installation you use to test the plugin.

- Windows with the .NET Framework 4.8 targeting pack
- .NET SDK capable of building SDK-style `net48` projects
- Torch installation folder containing `Torch.dll`, `Torch.API.dll`, and `NLog.dll`
- Space Engineers dedicated server `DedicatedServer64` folder containing `VRage.Game.dll` and `VRage.Library.dll`

Build from the repository root:

```powershell
dotnet build src/SE.Roleplay.Framework.Torch/SE.Roleplay.Framework.Torch.csproj -c Release -p:TorchLibPath="C:\Torch" -p:GameBinariesPath="C:\SpaceEngineersDedicated\DedicatedServer64"
```

Adjust both paths to match your installation. Do not copy proprietary game or Torch assemblies into this repository.

## Installation

1. Build the project against the exact Torch and SE dedicated-server versions used by your server.
2. Copy the generated plugin assembly and its framework dependencies to a plugin folder recognized by your Torch installation.
3. Include the generated `manifest.xml` with the plugin.
4. Add/enable the plugin in Torch and start a test world.
5. Confirm the log reports adapter initialization and profile load/save events.
6. Join the server and test the `!rp character` commands above.

Torch plugin packaging and deployment can differ between Torch releases. Validate this package against your installed Torch build before using it on a live world.

## Compatibility note

The adapter follows Torch's `TorchPluginBase`, `ITorchSessionManager`, `IMultiplayerManagerBase`, and `Torch.Commands` APIs. A successful compile against local runtime assemblies and an actual dedicated-server smoke test are still required before calling this adapter production-ready.

## Server-scoped character profiles

Character ownership and persistence belong to the server, not the desktop launcher. Each Torch instance constructs its own `PlayerIdentityService`, `PlayerProfileService`, and `JsonPlayerDataStore`. The store is rooted at that instance's `Storage/SE-Roleplay-Framework/Players` directory, so the same SteamID joining two different server instances receives two independent profile files and character lists.

The server-side profile API exposed through `IFrameworkContext.Profiles` provides:

- `LoadAsync` when a player joins (performed by the Torch adapter).
- `CreateCharacterAsync`, `GetCharactersAsync`, and `SelectCharacterAsync` for character management.
- `DeleteCharacterAsync` for character deletion.
- Automatic save/unload on disconnect and periodic server autosave.

The five-character limit is configured by `FrameworkOptions.MaximumCharactersPerPlayer` in the Torch adapter. Each server owner can set this independently.
