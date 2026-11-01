using System.Security.Cryptography;
using System.Text;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class DesktopAuthorizationCodeStoreTests
{
    private const string Client = "serpf-windows-launcher";
    private const string Redirect = "http://127.0.0.1:49152/oauth/callback/test";
    private const string Browser = "browser-session-id-with-more-than-32-characters";
    private const string SteamId = "76561198012345678";

    [Fact]
    public void CreateState_RejectsNonLoopbackAndUnregisteredClient()
    {
        var store = new DesktopAuthorizationCodeStore();
        var challenge = Challenge("verifier-string-that-is-long-enough-for-pkce-123");

        Assert.Throws<ArgumentException>(() =>
            store.CreateState("unknown-client", Redirect, challenge, Browser));
        Assert.Throws<ArgumentException>(() =>
            store.CreateState(Client, "http://localhost:49152/oauth/callback/test", challenge, Browser));
        Assert.Throws<ArgumentException>(() =>
            store.CreateState(Client, "https://127.0.0.1:49152/oauth/callback/test", challenge, Browser));
    }

    [Fact]
    public void State_IsBoundToBrowserAndCanBeCompletedOnlyOnce()
    {
        var store = new DesktopAuthorizationCodeStore();
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser);

        Assert.Null(store.CompleteState(state, Browser + "-other", SteamId));
        var code = store.CompleteState(state, Browser, SteamId);

        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Null(store.CompleteState(state, Browser, SteamId));
    }

    [Fact]
    public void Code_IsBoundToClientRedirectAndPkceVerifier()
    {
        var store = new DesktopAuthorizationCodeStore();
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser);
        var code = store.CompleteState(state, Browser, SteamId)!;

        Assert.Null(store.RedeemCode(code, "another-client", Redirect, verifier));
        Assert.Null(store.RedeemCode(code, Client, Redirect + "/changed", verifier));
        Assert.Null(store.RedeemCode(code, Client, Redirect, verifier + "wrong"));
        Assert.Null(store.RedeemCode(code, Client, Redirect, verifier));
    }

    [Fact]
    public void Code_RedeemsSuccessfullyOnce()
    {
        var store = new DesktopAuthorizationCodeStore();
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser);
        var code = store.CompleteState(state, Browser, SteamId)!;

        var grant = store.RedeemCode(code, Client, Redirect, verifier);

        Assert.NotNull(grant);
        Assert.Equal(SteamId, grant.SteamId64);
        Assert.Equal(Client, grant.ClientId);
        Assert.Null(store.RedeemCode(code, Client, Redirect, verifier));
    }

    [Fact]
    public async Task Code_CanBeRedeemedByOnlyOneConcurrentCaller()
    {
        var store = new DesktopAuthorizationCodeStore();
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser);
        var code = store.CompleteState(state, Browser, SteamId)!;

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => store.RedeemCode(code, Client, Redirect, verifier))));

        Assert.Single(results.Where(result => result is not null));
    }

    [Fact]
    public void CompleteState_RejectsMalformedSteamIdentity()
    {
        var store = new DesktopAuthorizationCodeStore();
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser);

        Assert.Null(store.CompleteState(state, Browser, "not-a-steam-id"));
    }

    [Fact]
    public void RedirectValidation_RejectsRemoteHostsAndQueryStrings()
    {
        Assert.True(DesktopAuthorizationCodeStore.IsLoopbackRedirect(Redirect));
        Assert.False(DesktopAuthorizationCodeStore.IsLoopbackRedirect("http://127.0.0.2:49152/oauth/callback/test"));
        Assert.False(DesktopAuthorizationCodeStore.IsLoopbackRedirect("http://127.0.0.1:49152/oauth/callback/test?next=elsewhere"));
        Assert.False(DesktopAuthorizationCodeStore.IsLoopbackRedirect("http://127.0.0.1:49152/other"));
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
