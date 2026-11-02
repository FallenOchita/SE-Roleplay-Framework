# JavaScript launcher UI

This folder contains the browser-side UI layer for the SE1 RP launcher. The desktop shell and all trusted application operations remain in C#.

## Language boundary

- JavaScript owns presentation behavior, client-side filtering, and rendering of server cards.
- C# owns Steam sign-in, filesystem access, game launching, saved credentials, and authoritative API calls.
- When hosted inside WebView2, messages must cross a narrow, versioned message bridge. Never expose arbitrary .NET reflection, process execution, filesystem access, or raw credentials to JavaScript.

## Bridge contract (version 1)

The host may send a servers.updated message with a servers array containing public listing fields only: id, name, description, address, port, players, maxPlayers, region, and status.

The page may request servers.refresh or server.connect with a server ID. The C# host must resolve the ID against its own trusted directory cache and validate the address and port; it must not accept a raw executable path or arbitrary command from page content.

The starter UI intentionally renders a clear unavailable state until a desktop host attaches the bridge.
