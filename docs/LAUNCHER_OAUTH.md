# Windows launcher directory sign-in

The Windows launcher connects its local Steam sign-in to the directory's desktop OAuth flow.

## Sign-in sequence

1. The launcher verifies the Steam OpenID assertion and obtains the local SteamID64.
2. If a directory HTTPS base URL is configured, the launcher creates a random OAuth state, a PKCE verifier, and an S256 challenge.
3. The system browser opens `/oauth/authorize`. The user signs into Steam on the directory website if needed and explicitly approves the launcher.
4. The directory redirects a one-time authorization code to an ephemeral `127.0.0.1` callback. The launcher checks the callback path and state before redeeming the code at `/oauth/token`.
5. The launcher calls `/api/launcher/me` with the access token and verifies that the browser-authorized SteamID64 exactly matches the SteamID64 verified by the launcher's own Steam sign-in.
6. The access token stays in memory. The refresh token is protected with Windows DPAPI for the current Windows user and stored in the launcher's settings file. On later starts, the launcher rotates the refresh token and verifies the same identity again.

## Requirements

- A deployed directory reachable over HTTPS.
- Directory configuration set to the HTTPS origin (for example, `https://directory.example.net`).
- Steam sign-in enabled and correctly configured on the directory.
- The launcher and browser must use the same Steam account during the two verification steps.

The launcher does not collect or store Steam passwords. If the directory is unavailable during startup, the saved refresh token is retained so the user can retry later.

## Current limitation

This PR establishes the authenticated desktop session and verifies its identity. Profile and owner-management screens still need to consume the in-memory bearer token; they are separate UI/API integration work.
