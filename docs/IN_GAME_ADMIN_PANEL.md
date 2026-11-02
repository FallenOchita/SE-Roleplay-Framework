# In-game server administration panel

## Goal

Add a graphical, clickable allowlist administration screen to the SE1 client mod while keeping the server authoritative. The Torch console commands remain supported. The desktop launcher must not gain allowlist or member-management controls.

This document defines the integration boundary before implementing the client screen and its server transport. It is not a claim that the graphical screen is already implemented.

## Components

- **Client mod:** owns the screen, input fields, confirmation prompts, loading/error states, and display of server responses.
- **Torch server adapter:** owns request handling, authorization, validation, rate limiting, and calls to the existing server-scoped directory API.
- **Directory API:** remains the persistence boundary for the server's allowlist. Its per-server API key stays in the Torch process.

The client must never receive `SERPF_SERVER_API_KEY`, the directory bootstrap key, or any credential that can manage the allowlist outside the active game session.

## Transport

Use Space Engineers' secure multiplayer message API, not ordinary chat text, for panel actions.

- Reserve one documented `ushort` channel ID for this framework and keep it stable across client and server builds.
- Register and unregister secure message handlers with the session lifecycle.
- Client requests go only to the server.
- Server responses go only to the requesting Steam account.
- Use a versioned, bounded payload format with a request ID so the UI can correlate responses.
- Reject malformed, oversized, unknown-version, unknown-operation, and unexpected-direction packets.
- Do not accept arbitrary URLs, HTTP methods, directory paths, server IDs, or API keys from the client.

### Request operations

The first panel iteration needs only these fixed operations:

| Operation | Fields | Server action |
| --- | --- | --- |
| `GetSnapshot` | request ID, protocol version | Read this server's allowlist state and members |
| `AddMember` | request ID, SteamID64, optional note | Validate and add one member |
| `RemoveMember` | request ID, SteamID64 | Validate and remove one member |
| `SetEnabled` | request ID, enabled | Enable or disable enforcement without deleting members |

The server derives the server ID and API key from its own Torch configuration. Neither is accepted from a client packet.

### Response operations

Every response includes the request ID, protocol version, success flag, and a bounded error code/message. Snapshot responses additionally include the enforcement state and member list. The client must treat server responses as untrusted display data and cap rendered text and collection sizes.

## Authorization and validation

Authorization is performed on the server for **every** request, not just when the screen opens.

1. Read the sender SteamID64 from the secure message callback metadata. Never trust a SteamID supplied inside the packet.
2. Confirm the packet arrived from a connected client in the expected direction.
3. Check the sender's current server-side admin/promote level using the authoritative SE1 session state. Do not rely on a client-side admin flag or a hidden UI button.
4. Apply per-sender rate limits and a maximum packet size before parsing or performing directory I/O.
5. Validate SteamID64 as exactly 17 decimal digits; cap notes at 200 characters.
6. Map the operation to one of the fixed directory calls already used by the Torch adapter. Never construct a directory URL from client-supplied path data.
7. Return a generic, bounded failure to unauthorized clients and log the detailed server-side reason without logging secrets.

The existing Torch command permissions remain in place. The GUI is an additional interface, not a replacement for console commands.

## UI behavior

The first screen should provide:

- Current allowlist enforcement state.
- Refresh button and loading indicator.
- Scrollable member list.
- Add-member input with optional note.
- Remove action with a confirmation step.
- Enable/disable toggle with confirmation.
- Clear success, validation, permission, timeout, and server-unavailable messages.
- Disabled controls while a mutation is pending, to prevent accidental duplicate submissions.

Opening the screen must not automatically mutate server state. Closing the screen must unregister no session-wide handlers; those belong to the session component and are removed during unload.

## Implementation sequence

1. Confirm the target SE1 client GUI API and supported screen lifecycle against the game's installed assemblies.
2. Add a client-side mod component and a screen that can open/close without network actions.
3. Add shared, versioned request/response DTOs and serialization tests.
4. Add the secure-message server handler to the Torch integration, with sender-derived identity, admin checks, rate limits, and strict operation mapping.
5. Add client request handling and UI state updates.
6. Test unauthorized requests, forged SteamID fields, malformed payloads, replay/duplicate clicks, server unload, timeout, and directory outage.
7. Build against the exact installed SE1 and Torch assemblies and smoke-test on a staging dedicated server with both an admin and a non-admin account.

## Current limitations

The repository currently has a Torch plugin but no dedicated client-side SE1 mod project. The Torch adapter targets .NET Framework 4.8 and depends on local proprietary Torch and game assemblies. CI does not currently build against those assemblies. The graphical panel and secure-message bridge are not implemented by this document; they require the client mod and Torch transport work described above.
