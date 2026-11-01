using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class SteamOpenIdRequestBuilderTests
{
    [Fact]
    public void Build_UsesSteamEndpointAndEncodedHttpsCallback()
    {
        var result = SteamOpenIdRequestBuilder.Build(
            "https://directory.example/auth/steam/callback?state=abc123",
            "https://directory.example");

        Assert.Equal(SteamOpenIdVerifier.SteamOpenIdEndpoint, result.GetLeftPart(UriPartial.Path));
        Assert.Contains("openid.mode=checkid_setup", result.Query);
        Assert.Contains(Uri.EscapeDataString("https://directory.example/auth/steam/callback?state=abc123"), result.Query);
        Assert.Contains("openid.claimed_id=", result.Query);
    }

    [Theory]
    [InlineData("http://directory.example/callback", "https://directory.example")]
    [InlineData("https://directory.example/callback", "http://directory.example")]
    [InlineData("https://directory.example/callback", "https://directory.example/?unsafe=query")]
    public void Build_RejectsNonHttpsOrNonCanonicalConfiguration(string returnTo, string realm)
    {
        Assert.Throws<ArgumentException>(() => SteamOpenIdRequestBuilder.Build(returnTo, realm));
    }
}
