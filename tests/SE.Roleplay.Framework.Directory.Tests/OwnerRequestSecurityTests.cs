using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class OwnerRequestSecurityTests
{
    [Theory]
    [InlineData("76561198012345678", true)]
    [InlineData("123", false)]
    [InlineData("7656119801234567x", false)]
    [InlineData("", false)]
    public void GetVerifiedSteamId_AcceptsOnly17DigitClaim(string steamId, bool expected)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, steamId) }, "test");
        var result = OwnerRequestSecurity.GetVerifiedSteamId(new ClaimsPrincipal(identity));
        Assert.Equal(expected ? steamId : null, result);
    }

    [Fact]
    public async Task ValidateMutation_RejectsMissingOrigin()
    {
        var context = new DefaultHttpContext();
        var result = await OwnerRequestSecurity.ValidateMutationAsync(
            context, null!, new Uri("https://directory.example/"));
        Assert.False(result);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("http://directory.example")]
    [InlineData("https://directory.example.evil.test")]
    public async Task ValidateMutation_RejectsMismatchedOrigin(string origin)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = origin;
        var result = await OwnerRequestSecurity.ValidateMutationAsync(
            context, null!, new Uri("https://directory.example/"));
        Assert.False(result);
    }
}
