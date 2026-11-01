# Directory server-owner authentication design

## Goal

Allow a community operator to sign in with Steam, register a Space Engineers 1 server, and manage only listings they own. The launcher and owner portal must never receive the directory's bootstrap administrator key.

This document defines the security boundary and API contract before the Steam OpenID flow is implemented.

## Identity and ownership

- Steam OpenID 2.0 is the identity provider. The service accepts only Steam's canonical claimed ID form: `https://steamcommunity.com/openid/id/{17-digit-SteamID64}`.
- The server validates the OpenID assertion by sending the complete assertion back to Steam with `openid.mode=check_authentication`; a browser redirect or client-supplied SteamID is never proof.
- The callback must match a one-time login state created by the service. The state is random, short-lived, bound to the initiating browser session, and consumed on callback.
- The server derives the SteamID64 from the verified claimed ID. It never accepts an owner SteamID in a registration or ownership-change request.
- Each directory listing stores an immutable owner SteamID64. Existing listings created with the bootstrap admin endpoint remain admin-managed until explicitly claimed through a separate, audited admin operation.
- One Steam account may own multiple listings. A listing has exactly one owner in the initial implementation; shared staff roles are a later feature.

## Browser session

- Use ASP.NET Core cookie authentication with secure, HttpOnly, SameSite=Lax cookies and server-side Data Protection keys persisted outside the application working directory.
- Require HTTPS in production and enable HSTS. Configure trusted reverse-proxy forwarding explicitly; do not trust arbitrary forwarded headers.
- Set short login-state expiry (5 minutes), rotate session identifiers after successful sign-in, and make logout a POST protected by antiforgery.
- Do not store Steam access tokens or passwords; Steam OpenID does not provide either.
- Apply rate limits to login initiation, callback validation, registration, and management endpoints. Avoid logging OpenID assertions, cookies, API keys, or full callback query strings.

## Proposed routes

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/auth/steam/login` | Create one-time state and redirect to Steam OpenID |
| GET | `/auth/steam/callback` | Validate assertion with Steam and establish owner session |
| POST | `/auth/logout` | End the owner session |
| GET | `/api/owner/me` | Return verified Steam account identity |
| GET | `/api/owner/servers` | List only the signed-in owner's servers |
| POST | `/api/owner/servers` | Register a server owned by the signed-in account; return its API key once |
| PATCH | `/api/owner/servers/{id}` | Update listing metadata after owner check |
| DELETE | `/api/owner/servers/{id}` | Delete only the owner's listing |
| POST | `/api/owner/servers/{id}/rotate-key` | Replace the server API key and return the new key once |

The existing public feed and Torch heartbeat route remain unchanged. Existing bootstrap-admin routes remain available for emergency administration, but their key is server-side only and must never be included in launcher configuration.

## Authorization rules

Every owner route must derive the SteamID64 from the authenticated server-side principal, then perform the ownership check inside the directory store's serialized operation. Do not perform a check in the API layer and then mutate by ID in a separate unguarded operation.

For update, delete, and key rotation:
1. Load the listing while holding the store's write gate.
2. Compare its stored owner SteamID64 with the authenticated principal using ordinal comparison.
3. Return the same not-found response for a missing listing and a listing owned by someone else.
4. Mutate and atomically persist while still holding the gate.

The generated per-server API key is random, returned only once, and persisted only as a SHA-256 hash. Rotation invalidates the previous key immediately. Heartbeat requests continue to authenticate using the per-server key and do not use browser cookies.

## Registration and ownership migration

- New owner-portal registrations are assigned to the verified SteamID64 from the session.
- The public server feed never includes owner SteamIDs, API-key hashes, or other credentials.
- Bootstrap-admin-created records have no owner by default. Do not automatically assign them to whichever Steam account first logs in.
- An admin-only claim operation may be added later with an explicit verification workflow and audit record. Until then, legacy records stay admin-managed.
- The first implementation should not support arbitrary transfer of ownership. A later transfer flow must require confirmation by the current owner and a fresh sign-in by the receiving account.

## Persistence migration

Add nullable `OwnerSteamId64` to directory records. Existing JSON documents deserialize with a null owner and must continue to load. Do not rename or repurpose the existing `ApiKeyHash` field. Keep the current single-process JSON-store limitation documented; move to a transactional shared database before running multiple service instances.

## Required automated tests

- Reject malformed, unsigned, expired, replayed, or mismatched Steam OpenID callbacks.
- Reject claimed IDs outside the canonical Steam community URL and reject non-17-digit IDs.
- Verify that callback state is single-use and bound to the initiating browser session.
- Registration assigns the verified principal, not a request-body owner field.
- Owner A cannot list, update, delete, or rotate credentials for Owner B's listing.
- Missing and non-owned listing IDs produce indistinguishable responses.
- Key rotation invalidates the old key and returns the new key only in the rotation response.
- Public feed omits owner identity and API-key hashes.
- Existing JSON records without an owner still load as admin-managed.

## Implementation sequence

1. Add nullable owner identity to the persisted model and tests for backward compatibility.
2. Implement and test the Steam OpenID 2.0 verifier as an isolated service.
3. Add secure cookie session and one-time login-state handling.
4. Add owner-scoped store operations and routes.
5. Add key rotation, rate limiting, audit events, and deployment guidance.
6. Build an owner-management UI in the launcher only after the service API is validated.

No production owner-registration route should be enabled until the OpenID verifier, state/replay protections, owner authorization tests, and HTTPS deployment requirements are in place.

## Implementation status

- Complete: Steam OpenID assertion verification, browser-bound single-use login state, HTTPS-only login redirect configuration, protected cookie sign-in, and persisted Data Protection keys.
- In progress: nullable `OwnerSteamId64` persistence and owner-scoped store operations. These operations compare the verified SteamID64 and perform authorization and mutation under the same store gate. Public-feed projections omit owner identity and API-key hashes.
- Not yet enabled: owner HTTP routes, logout, antiforgery/Origin validation, owner portal, and launcher owner UI. Do not expose owner-management routes until browser CSRF protections, rate limits, request validation, route integration tests, and owner-isolation tests are complete.
- Legacy records remain ownerless. No automatic claim or transfer is performed.

The current JSON store remains single-process only. A multi-instance deployment must move ownership and key updates to shared transactional storage with atomic authorization checks.
