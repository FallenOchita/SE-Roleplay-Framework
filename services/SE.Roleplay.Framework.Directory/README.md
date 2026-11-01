# SE Roleplay Framework Directory Service

A self-hosted directory API for the **Space Engineers 1** launcher. It publishes community server listings and accepts authenticated heartbeat updates from registered servers.

## Run

Requires the .NET 8 SDK/runtime.

Set these environment variables before starting:

- SERPF_DIRECTORY_ADMIN_KEY — a long, random secret used for registration/deletion.
- SERPF_DIRECTORY_DATA_PATH — optional path for the JSON data file.

Run:

    dotnet run --project services/SE.Roleplay.Framework.Directory

Host this service behind an HTTPS reverse proxy in production. Never expose the admin key in a launcher build or commit it to source control.

## Endpoints

- GET /health — health check.
- GET /api/servers — public server feed. The response uses the launcher's { "servers": [...] } format.
- POST /api/admin/servers — register a server. Requires X-Directory-Admin-Key.
- PUT /api/servers/{id}/heartbeat — update player count/version. Requires X-Server-Api-Key.
- DELETE /api/admin/servers/{id} — remove a listing. Requires X-Directory-Admin-Key.

## Register a server

POST /api/admin/servers with the X-Directory-Admin-Key header and JSON body:

    {
      "name": "Example RP Community",
      "description": "Persistent SE1 roleplay",
      "address": "play.example.net",
      "port": 27016,
      "maxPlayers": 32,
      "region": "North America",
      "websiteUrl": "https://example.net",
      "discordUrl": "https://discord.gg/example",
      "rulesUrl": "https://example.net/rules",
      "tags": ["roleplay", "survival"]
    }

The response contains a generated server ID and a random API key. Save that key securely; it is returned only during registration. Only its SHA-256 hash is persisted by the directory.

## Send a heartbeat

PUT /api/servers/SERVER_ID/heartbeat with the X-Server-Api-Key header and JSON body:

    { "players": 4, "maxPlayers": 32, "version": "SE1 server version" }

Listings become Offline when no heartbeat has been received for 90 seconds. A Torch heartbeat sender is a follow-up integration; this API can first be exercised with a small server-side agent or manual HTTP request.

## Security notes

- Configure a unique, high-entropy admin key using the environment variable.
- Put the service behind HTTPS; never send API keys over plaintext remote HTTP.
- Server API keys are generated cryptographically, stored as SHA-256 hashes, and compared using fixed-time verification.
- The public feed never includes credential hashes.
- The JSON store uses a process-local async lock and atomic file replacement. Run one service instance per data file; use a shared database before horizontal scaling.

## Steam login state groundwork

The service registers an internal `SteamLoginStateStore` for the upcoming owner sign-in flow. It creates 256-bit cryptographically random URL-safe state values, stores only SHA-256 digests, binds each state to a server-generated browser-session identifier, and allows one successful consume within a maximum five-minute lifetime. Expired entries are pruned during store operations, and consumption is atomic within this process.

This is groundwork only: no Steam login or owner-management routes are enabled yet. The future login endpoint must issue the browser-session identifier in a Secure, HttpOnly, SameSite=Lax cookie over HTTPS, pass that server-read cookie value to the state store, and never accept the binding identifier from request parameters or a request body. The current in-memory state store is process-local; a multi-instance deployment will require shared state storage or sticky routing with equivalent replay guarantees.
