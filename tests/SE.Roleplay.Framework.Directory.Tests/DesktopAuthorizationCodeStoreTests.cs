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
        Assert.Null(CreateCode(store, verifier, Client, Redirect)
            .Redeem(store, "another-client", Redirect, verifier));
        Assert.Null(CreateCode(store, verifier, Client, Redirect)
            .Redeem(store, Client, Redirect + "/changed", verifier));
        Assert.Null(CreateCode(store, verifier, Client, Redirect)
            .Redeem(store, Client, Redirect, verifier + "wrong"));
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

    [Fact]
    public void State_ExpiresAfterFiveMinutes()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new DesktopAuthorizationCodeStore(clock);
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser);
        clock.Advance(DesktopAuthorizationCodeStore.StateLifetime);
        Assert.Null(store.CompleteState(state, Browser, SteamId));
    }

    [Fact]
    public void Code_ExpiresAfterSixtySeconds()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new DesktopAuthorizationCodeStore(clock);
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser);
        var code = store.CompleteState(state, Browser, SteamId)!;
        clock.Advance(DesktopAuthorizationCodeStore.CodeLifetime);
        Assert.Null(store.RedeemCode(code, Client, Redirect, verifier));
    }

    [Fact]
    public void Approval_PreservesOriginalLauncherStateAndExactRedirect()
    {
        var store = new DesktopAuthorizationCodeStore();
        const string launcherState = "launcher-correlation-state-123";
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser, launcherState);

        var approval = store.CompleteAuthorization(state, Browser, SteamId);

        Assert.NotNull(approval);
        Assert.Equal(Redirect, approval.RedirectUri);
        Assert.Equal(launcherState, approval.ClientState);
        Assert.False(string.IsNullOrWhiteSpace(approval.Code));
        Assert.Null(store.CompleteAuthorization(state, Browser, SteamId));
    }

    [Fact]
    public void Denial_ConsumesTransactionAndReturnsOnlyStoredCallbackDetails()
    {
        var store = new DesktopAuthorizationCodeStore();
        const string launcherState = "launcher-correlation-state-456";
        var verifier = "verifier-string-that-is-long-enough-for-pkce-123";
        var state = store.CreateState(Client, Redirect, Challenge(verifier), Browser, launcherState);

        Assert.Null(store.DenyAuthorization(state, Browser + "-different"));
        var denial = store.DenyAuthorization(state, Browser);

        Assert.NotNull(denial);
        Assert.Equal(Redirect, denial.RedirectUri);
        Assert.Equal(launcherState, denial.ClientState);
        Assert.Null(store.DenyAuthorization(state, Browser));
        Assert.Null(store.CompleteAuthorization(state, Browser, SteamId));
    }

    private static (string Code, Func<DesktopAuthorizationCodeStore, string, string, string, DesktopAuthorizationGrant?> Redeem)
        CreateCode(DesktopAuthorizationCodeStore store, string verifier, string clientId, string redirectUri)
    {
        var state = store.CreateState(clientId, redirectUri, Challenge(verifier), Browser);
        var code = store.CompleteState(state, Browser, SteamId)!;
        return (code, (target, client, redirect, proof) => target.RedeemCode(code, client, redirect, proof));
    }

    private sealed class ManualClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
