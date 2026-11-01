using System.Net;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class SteamOpenIdVerifierTests
{
    private const string SteamId = "76561198012345678";
    private const string ReturnTo = "https://directory.example/auth/steam/callback?state=single-use-state";

    [Fact]
    public async Task VerifyAsync_ReturnsSteamId_WhenSteamConfirmsSignedAssertion()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(SteamOpenIdVerifier.SteamOpenIdEndpoint, request.RequestUri?.AbsoluteUri);
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("openid.mode=check_authentication", body);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")
            });
        });
        var verifier = new SteamOpenIdVerifier(new HttpClient(handler));

        var result = await verifier.VerifyAsync(CreateAssertion(), ReturnTo);

        Assert.Equal(SteamId, result);
    }

    [Fact]
    public async Task VerifyAsync_RejectsAssertion_WhenSteamDoesNotConfirmIt()
    {
        var verifier = new SteamOpenIdVerifier(new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("is_valid:false\n")
            }))));

        var result = await verifier.VerifyAsync(CreateAssertion(), ReturnTo);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_RejectsClaimedIdThatIsNotCanonicalSteamId()
    {
        var assertion = CreateAssertion();
        assertion["openid.claimed_id"] = "https://example.com/openid/id/" + SteamId;
        assertion["openid.identity"] = assertion["openid.claimed_id"];
        var verifier = new SteamOpenIdVerifier(new HttpClient(new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Malformed identities must be rejected before contacting Steam."))));

        var result = await verifier.VerifyAsync(assertion, ReturnTo);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_RejectsRequiredFieldsThatAreNotSigned()
    {
        var assertion = CreateAssertion();
        assertion["openid.signed"] = "op_endpoint,return_to,response_nonce,assoc_handle";
        var verifier = new SteamOpenIdVerifier(new HttpClient(new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Unsigned assertions must be rejected before contacting Steam."))));

        var result = await verifier.VerifyAsync(assertion, ReturnTo);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyAsync_RejectsReplayedNonce()
    {
        var verifier = new SteamOpenIdVerifier(new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("is_valid:true\n")
            }))));
        var assertion = CreateAssertion();

        var first = await verifier.VerifyAsync(assertion, ReturnTo);
        var second = await verifier.VerifyAsync(assertion, ReturnTo);

        Assert.Equal(SteamId, first);
        Assert.Null(second);
    }

    [Fact]
    public async Task VerifyAsync_RejectsCallbackUrlThatIsNotHttps()
    {
        var verifier = new SteamOpenIdVerifier(new HttpClient(new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Invalid callback URL must be rejected before contacting Steam."))));

        var result = await verifier.VerifyAsync(CreateAssertion(), "http://directory.example/callback?state=x");

        Assert.Null(result);
    }

    private static Dictionary<string, string> CreateAssertion()
    {
        var nonce = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") + "test-nonce";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = SteamOpenIdVerifier.SteamOpenIdEndpoint,
            ["openid.claimed_id"] = "https://steamcommunity.com/openid/id/" + SteamId,
            ["openid.identity"] = "https://steamcommunity.com/openid/id/" + SteamId,
            ["openid.return_to"] = ReturnTo,
            ["openid.response_nonce"] = nonce,
            ["openid.assoc_handle"] = "test-association",
            ["openid.signed"] = "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
            ["openid.sig"] = "test-signature"
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
            _send = send;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _send(request, cancellationToken);
    }
}
