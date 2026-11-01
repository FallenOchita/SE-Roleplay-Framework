# Desktop launcher owner authentication handoff

## Status

Design proposal only. The current directory owner API authenticates browser requests with the protected `__Host-serpf-owner` cookie. The WPF launcher has a separate Steam OpenID flow that verifies an account but does not create a directory owner session. These credentials must not be treated as interchangeable.

Do not copy browser cookies into the launcher, scrape browser cookie stores, accept a SteamID supplied by the launcher, or put the directory bootstrap admin key in the launcher.

## Goal

Allow the official Windows launcher to manage only the listings owned by the Steam account that completed Steam sign-in, while keeping Steam credentials on Steam's website and preserving the directory service as the authority for identity and ownership.

## Proposed flow: authorization code + PKCE over loopback

1. The launcher starts a temporary HTTP listener bound only to `127.0.0.1` on an OS-assigned ephemeral port. It generates a cryptographically random `state` and PKCE verifier, computes the S256 challenge, and records the exact callback URI for this login attempt.
2. The launcher opens the configured directory's HTTPS desktop-authorization endpoint in the system browser. It supplies a fixed public launcher client identifier, exact loopback redirect URI, state, and S256 challenge. The launcher is a public client; a client secret must never be embedded in its executable.
3. The directory validates the client identifier, loopback IP, ephemeral port, callback path, state length, and S256 challenge format. It stores a short-lived authorization transaction bound to the existing browser-session cookie and the requested callback details, then redirects to Steam OpenID.
4. Steam returns to the directory's existing OpenID callback. The directory consumes the browser-bound login state, validates the complete Steam assertion with Steam, and derives the SteamID64 itself. It must not use any launcher-supplied identity.
5. After successful verification, the directory creates a high-entropy, single-use authorization code (store only its hash) bound to the verified SteamID64, PKCE challenge, exact redirect URI, and client identifier. The code expires quickly (target: 60 seconds). The directory redirects the browser to the exact loopback callback with the code and original state.
6. The listener verifies state using a constant-time comparison, accepts only the expected callback path and one callback, then shuts down. It does not trust the SteamID or any other identity in callback query parameters.
7. The launcher exchanges the code over HTTPS at a dedicated token endpoint, sending the verifier, code, redirect URI, and client identifier. The directory atomically consumes the code, checks expiry and all bindings, validates the S256 verifier, and issues a short-lived access token plus a rotating opaque refresh token.
8. The launcher stores the refresh token using Windows DPAPI with CurrentUser scope. Access tokens remain in memory and are short-lived. The launcher never writes tokens or authorization codes to logs.
9. Owner API routes accept the directory-issued bearer access token and derive SteamID64 from validated token claims. Browser cookie authentication continues to work for the web owner flow. The owner API must never accept a SteamID from a request body, query string, or unverified header.
10. Refresh rotates the refresh token atomically; reuse of an already-rotated token revokes the token family. Logout/revoke invalidates the refresh token family. Access tokens have a short lifetime so revocation delay is bounded.

## Required security properties

- HTTPS is mandatory for every directory endpoint except the local loopback callback.
- Bind the listener to IPv4 loopback only, not `0.0.0.0`, wildcard interfaces, or LAN addresses. Use a random port and a non-guessable callback path.
- Validate exact redirect URI equality against the URI registered by the launcher transaction. Do not accept arbitrary hosts, ports, paths, schemes, fragments, or redirect wildcards.
- Accept only loopback IP literal redirect hosts (`127.0.0.1`); reject DNS names, IPv6 unless separately and deliberately implemented, and non-loopback destinations.
- Use PKCE S256 only; reject `plain`, missing challenges, malformed verifiers, and verifier mismatch.
- State and authorization codes are high entropy, short-lived, single-use, and stored hashed. Consumption and replay prevention must be atomic across service instances.
- Bind the browser authorization transaction to the browser-session cookie as well as state. Keep the existing Steam OpenID nonce replay defense.
- Do not redirect to the launcher until Steam has confirmed the OpenID assertion.
- Access tokens must be audience- and issuer-validated, have narrow owner-management scopes, and expire quickly. Signing keys must be protected and rotated operationally.
- Refresh tokens must be opaque, hashed at rest, rotated on every use, and revocable. Reuse detection must revoke the full token family.
- The launcher must protect refresh tokens with Windows DPAPI CurrentUser and must not sync or export them in plain text.
- Preserve owner isolation inside each serialized store operation. Never rely on client-side filtering for authorization.
- Keep rate limits on authorization, token exchange, refresh, and owner mutations. Avoid logging codes, tokens, PKCE verifiers, cookies, or Steam assertions.
- Maintain the browser cookie flow. Do not weaken CSRF and exact-Origin checks for browser-cookie requests. Bearer-token requests should be authenticated by the token handler and remain HTTPS-only.

## Implementation sequence

1. Add isolated models and stores for desktop authorization transactions, one-time authorization codes, and refresh-token families. Add concurrency and replay tests before exposing routes.
2. Add the launcher authorization initiation and loopback callback endpoints with exact redirect validation and browser-session binding. Add integration tests for mismatched state, callback URI, client ID, expired/replayed state, and failed Steam verification.
3. Add code exchange, token validation, refresh rotation, and revocation. Test wrong verifier, wrong redirect, wrong audience, expiry, replay, refresh reuse, and cross-client isolation.
4. Add bearer authentication to owner routes without changing cookie authentication behavior. Test that browser and desktop principals are independently authenticated and both remain owner-scoped.
5. Add a launcher owner client that uses only the directory-issued access token, DPAPI-protected refresh token storage, and explicit sign-out.
6. Add an Owner Servers page for listing, registration, editing, deletion, and key rotation. Display a newly issued server API key only once with a clear copy/save warning; never persist it in launcher settings.
7. Add end-to-end tests and a Windows smoke-test checklist before marking the launcher flow production-ready.

## Explicit non-goals

- No embedded Steam login form or collection of Steam credentials.
- No copying or importing browser cookies.
- No client-provided SteamID authentication.
- No directory bootstrap admin key in the launcher.
- No global character store. Character records remain owned by individual Torch servers.
- No long-lived bearer access token or unprotected refresh token in launcher settings.
