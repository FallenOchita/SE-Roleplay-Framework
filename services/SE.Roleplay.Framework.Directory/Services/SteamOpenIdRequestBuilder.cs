namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>Builds a Steam OpenID 2.0 authentication request using only configured HTTPS URLs.</summary>
public static class SteamOpenIdRequestBuilder
{
    public static Uri Build(string returnTo, string realm)
    {
        if (!Uri.TryCreate(returnTo, UriKind.Absolute, out var callback) ||
            callback.Scheme != Uri.UriSchemeHttps ||
            !Uri.TryCreate(realm, UriKind.Absolute, out var realmUri) ||
            realmUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(realmUri.Query) ||
            !string.IsNullOrEmpty(realmUri.Fragment) ||
            realmUri.AbsolutePath != "/" ||
            !string.Equals(callback.GetLeftPart(UriPartial.Authority), realmUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Steam login requires absolute HTTPS callback and realm URLs.");

        var parameters = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = callback.AbsoluteUri,
            ["openid.realm"] = realmUri.AbsoluteUri.TrimEnd('/') + "/",
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
        };

        var query = string.Join("&", parameters.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return new Uri(SteamOpenIdVerifier.SteamOpenIdEndpoint + "?" + query, UriKind.Absolute);
    }
}
