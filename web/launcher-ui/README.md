# JavaScript launcher UI

Primary browser-based community discovery for the SE1 RP launcher.

## Features
- Search by server name, description, region, or address.
- Filter online, offline, and favorited communities.
- Save favorites in the WebView2 browser profile.
- Show player counts and connection status.
- Connect from a server card.

## Trust boundary
JavaScript owns presentation and client-side filtering. C# owns Steam sign-in, filesystem access, game launching, saved credentials, and authoritative API calls. The versioned WebView2 bridge exposes no arbitrary .NET reflection, process execution, filesystem access, or credentials.

The host sends `servers.updated` with public listing fields only. The page requests `servers.refresh` or `server.connect` with a server ID. C# resolves that ID against its trusted directory cache and validates the endpoint; the page never supplies a raw command or executable path.

The native WPF server list remains available as a fallback.