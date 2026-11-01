# Desktop Launcher OAuth Endpoint Contract

This document defines the HTTP integration that follows the merged one-time PKCE code store. It is a contract and implementation checklist; it does not claim that these endpoints are implemented.

## Flow

1. The launcher generates a high-entropy PKCE verifier, derives an S256 challenge, generates an independent random state value, and starts a loopback HTTP listener on an IPv4 loopback address.
2. The launcher opens the directory authorization URL in the system browser with the fixed client ID, exact loopback redirect URI, S256 challenge, and launcher-generated state.
3. The directory requires a verified Steam browser session and displays a consent page identifying the client and requested account identity. Merely opening the URL must never grant authorization.
4. On explicit approval, the directory creates a short-lived authorization transaction bound to the browser session, verified SteamID64, client ID, exact redirect URI, PKCE challenge, and the launcher's original state.
5. The directory redirects only to the exact validated loopback URI, with a one-time authorization code and the original state. Denial returns an error and the same state.
6. The launcher exchanges the code, exact redirect URI, client ID, and PKCE verifier at the token endpoint. The directory atomically consumes the code before issuing tokens.
7. The launcher uses the short-lived access token as a bearer credential. Refresh tokens are rotated on every refresh; reuse of an old refresh token revokes the associated token family.

## Endpoint contract

### GET /oauth/authorize
- Requires a verified Steam cookie session.
- Accepts only the registered client ID serpf-windows-launcher.
- Validates the redirect URI using DesktopAuthorizationCodeStore.IsLoopbackRedirect and validates the PKCE S256 challenge format.
- Requires a bounded, non-empty client state value.
- Returns or renders a consent screen. It must not issue a code on GET.
- Applies rate limiting and no-store/no-cache response headers.

### POST /oauth/authorize/approve
- Requires the same authenticated browser session that initiated authorization.
- Requires same-origin validation and an antiforgery token.
- Requires an explicit approval action.
- Completes the stored state once and redirects only to the exact stored loopback URI.
- Returns the original launcher state unchanged; never accept a replacement redirect URI or challenge from the approval POST.
- A denial consumes the pending transaction and redirects with error=access_denied and the original state.

### POST /oauth/token
- Accepts grant_type=authorization_code with code, client ID, redirect URI, and PKCE verifier.
- Redeems the code atomically. Wrong client, redirect URI, verifier, expired code, or replay must fail without disclosing which check failed.
- Returns an opaque short-lived access token and a rotating refresh token. Do not return Steam cookies, Steam OpenID assertions, server-owner API keys, or directory admin secrets.
- Also accepts grant_type=refresh_token. Rotation is atomic and one-time; detected reuse revokes the token family.
- Uses Cache-Control: no-store and Pragma: no-cache on token responses.

### Bearer-protected launcher APIs
- Validate opaque access tokens server-side, including expiry and revocation.
- Derive Steam identity only from the validated token record; never accept a SteamID64 supplied by the client as authentication.
- Return 401 for missing, expired, malformed, or revoked credentials.
- Keep browser cookie authentication and launcher bearer authentication as separate schemes.

## Storage and deployment requirements
- The current DesktopAuthorizationCodeStore is process-local. It is suitable only for a single directory process.
- Before multi-instance deployment, replace state, code, access-token, and refresh-token storage with a shared store that supports atomic consume/rotate operations.
- Store only hashes of opaque authorization codes, access tokens, and refresh tokens.
- Apply bounded lifetimes, cleanup, rate limits, and generic failure responses.
- Persist refresh-token family revocation so it survives process restarts.
- Do not ship or enable the launcher login flow until the endpoint integration tests below pass.

## Required integration tests
- Unauthenticated authorization cannot grant a code; Steam login must return the user to the pending authorization flow.
- Consent is explicit; GET alone never creates a redeemable code.
- State, browser-session binding, Steam identity, client ID, redirect URI, and PKCE challenge are all bound to one transaction.
- Approval, denial, code redemption, and refresh rotation are single-use under concurrent requests.
- Redirect validation rejects DNS names, non-loopback addresses, unexpected paths, user-info, query strings, and fragments.
- Token responses are non-cacheable; access tokens expire; revoked and expired tokens fail.
- Refresh-token reuse revokes the full token family.
- CSRF, wrong-origin, rate-limit, malformed-input, and replay cases are covered.