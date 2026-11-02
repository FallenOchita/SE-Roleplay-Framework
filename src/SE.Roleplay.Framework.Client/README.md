# SE1 client mod

This project is the client-side half of the in-game server administration panel. It is separate from the desktop launcher.

## Current implementation

- Registers a secure multiplayer message handler on channel `51347`.
- Opens the in-game administration screen with **F6** for a local server admin.
- Provides clickable refresh, add-member, remove-member, and enforcement-toggle controls.
- Sends versioned, bounded JSON requests to the server and accepts responses only from the server.
- Does not contain a directory URL, server ID, or server API key.

The client-side admin check only controls whether the panel is shown. It is not an authorization boundary. The server must derive the sender SteamID from the secure-message callback and independently check the sender's current server admin level before performing every operation.

## Build

Build against the exact installed SE1 game binaries:

```powershell
dotnet build src/SE.Roleplay.Framework.Client/SE.Roleplay.Framework.Client.csproj -c Release -p:GameBinariesPath="C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64"
```

The output assembly must be packaged into the mod's `Data/Scripts/SE.Roleplay.Framework.Client/` folder.

## Integration status

The client project is an initial implementation. It requires the matching server-side secure-message handler using channel `51347`, the same message contract, and server-side authorization. The Torch console commands remain available. The GUI must not be treated as functional until that server bridge exists and the client mod is built and tested against the target SE1 installation.
