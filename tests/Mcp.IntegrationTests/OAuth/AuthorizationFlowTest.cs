using System.Net;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(OAuthCollection))]
public sealed class AuthorizationFlowTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    private const string RedirectUri = "https://c.example/cb";

    [Fact]
    public async Task GuestShouldBeSentToConsentPageWithQueryIntact()
    {
        // arrange
        var clientId = await RegisterClient(RedirectUri);
        var url = AuthorizeUrl(clientId, RedirectUri, NewPkce(), "st", "mcp", null);

        // act
        var response = await Http.GetAsync(url);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/oauth/consent?");
        location.Should().Contain("client_id=" + clientId).And.Contain("state=st").And.Contain("code_challenge=",
            because: "the consent page re-requests /oauth/authorize with the original query");
    }

    [Fact]
    public async Task SignedInUserWithoutGrantShouldBeSentToConsent()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), "st", "mcp", null));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/oauth/consent?");
    }

    [Fact]
    public async Task ApprovedRequestShouldRedirectWithCodeAndState()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);

        // act
        var response = await Authorize(clientId, RedirectUri, NewPkce(), state: "xyz");

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        GetQueryValue(location, "code").Should().NotBeEmpty();
        GetQueryValue(location, "state").Should().Be("xyz");
    }

    [Fact]
    public async Task DeniedRequestShouldRedirectWithAccessDenied()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);

        // act
        var response = await Authorize(clientId, RedirectUri, NewPkce(), state: "d", approve: false);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        GetQueryValue(location, "error").Should().Be("access_denied");
        GetQueryValue(location, "state").Should().Be("d");
    }

    [Fact]
    public async Task UnknownClientShouldNotRedirect()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl("nope", RedirectUri, NewPkce(), null, "mcp", null));

        // assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect,
            because: "an unknown client must not receive a redirect to its redirect_uri");
    }

    [Fact]
    public async Task WrongRedirectUriShouldNotRedirect()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);

        // act
        var response = await SendAsUser(
            HttpMethod.Get, AuthorizeUrl(clientId, "https://evil.example/cb", NewPkce(), null, "mcp", null));

        // assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect,
            because: "an unregistered redirect_uri must never be redirected to");
    }

    [Fact]
    public async Task LoopbackRedirectShouldIgnorePort()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("http://localhost/cb");

        // act
        var response = await Authorize(clientId, "http://localhost:53211/cb", NewPkce());

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be("http://localhost:53211/cb",
            because: "RFC 8252 lets a loopback client pick an ephemeral port per session");
    }

    [Fact]
    public async Task CodeExchangeShouldIssueJwtWithSessionClaims()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var pkce = NewPkce();
        var code = GetQueryValue((await Authorize(clientId, RedirectUri, pkce)).Headers.Location!, "code");

        // act
        var (status, tokens) = await ExchangeCode(clientId, RedirectUri, code, pkce.Verifier);

        // assert
        status.Should().Be(HttpStatusCode.OK);
        tokens.GetProperty("token_type").GetString().Should().Be("Bearer");
        tokens.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
        var jwt = ReadJwt(tokens.GetProperty("access_token").GetString()!);
        jwt.Subject.Should().Be(alice.Id.Value);
        jwt.Claims.Should().Contain(c => c.Type == "sid" && c.Value.StartsWith("@"),
            because: "the token must carry its OAuth backing session id");
        jwt.Claims.Should().Contain(c => c.Type == "client_id" && c.Value == clientId);
        jwt.Audiences.Should().Contain(new Uri(BaseUri, "/mcp").ToString(),
            because: "the MCP endpoint is the default audience");
        string.Join(' ', jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value)).Should().Contain("mcp");
        (jwt.ValidTo - jwt.IssuedAt).Should().BeLessThan(TimeSpan.FromSeconds(10),
            because: "the test fixture sets a 3s access token lifetime");
    }

    [Fact]
    public async Task ResourceParameterShouldBecomeAudience()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var pkce = NewPkce();
        var resource = new Uri(BaseUri, "/mcp").ToString();
        var code = GetQueryValue(
            (await Authorize(clientId, RedirectUri, pkce, resource: resource)).Headers.Location!, "code");

        // act
        var (status, tokens) = await ExchangeCode(clientId, RedirectUri, code, pkce.Verifier, resource);

        // assert
        status.Should().Be(HttpStatusCode.OK);
        ReadJwt(tokens.GetProperty("access_token").GetString()!).Audiences.Should().Contain(resource);
    }

    [Fact]
    public async Task WrongVerifierShouldBeInvalidGrant()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var code = GetQueryValue((await Authorize(clientId, RedirectUri, NewPkce())).Headers.Location!, "code");

        // act
        var (status, body) = await ExchangeCode(clientId, RedirectUri, code, NewPkce().Verifier);

        // assert
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ReusedCodeShouldBeInvalidGrant()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var pkce = NewPkce();
        var code = GetQueryValue((await Authorize(clientId, RedirectUri, pkce)).Headers.Location!, "code");
        (await ExchangeCode(clientId, RedirectUri, code, pkce.Verifier)).Item1.Should().Be(HttpStatusCode.OK);

        // act
        var (status, body) = await ExchangeCode(clientId, RedirectUri, code, pkce.Verifier);

        // assert
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_grant", because: "a code is single-use");
    }

    [Fact]
    public async Task RefreshShouldRotateAndKillOldToken()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var oldRefresh = tokens.GetProperty("refresh_token").GetString()!;

        // act
        var (status, refreshed) = await Refresh(clientId, oldRefresh);
        var (replayStatus, replay) = await Refresh(clientId, oldRefresh);

        // assert
        status.Should().Be(HttpStatusCode.OK);
        refreshed.GetProperty("refresh_token").GetString().Should().NotBe(oldRefresh,
            because: "refresh tokens rotate on every use");
        ReadJwt(refreshed.GetProperty("access_token").GetString()!).Claims.Should().Contain(c => c.Type == "sid",
            because: "the refreshed access token must keep the backing session id");
        replayStatus.Should().Be(HttpStatusCode.BadRequest);
        replay.GetProperty("error").GetString().Should().Be("invalid_grant",
            because: "a rotated-out refresh token is dead");
    }

    [Fact]
    public async Task RefreshShouldTouchBackingSession()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var sid = ReadJwt(tokens.GetProperty("access_token").GetString()!).Claims.Single(c => c.Type == "sid").Value;
        var sessionsBackend = Tester.AppServices.GetRequiredService<ISessionsBackend>();
        var before = (await sessionsBackend.Get(new Session(sid), default))!;
        await Task.Delay(1100);

        // act
        await Refresh(clientId, tokens.GetProperty("refresh_token").GetString()!);

        // assert
        var after = await TestWait.When(async ct => {
            var sessionInfo = (await sessionsBackend.Get(new Session(sid), ct))!;
            sessionInfo.LastSeenAt.Should().BeGreaterThan(before.LastSeenAt,
                because: "a refresh is activity on the backing session");
            return sessionInfo;
        }, TimeSpan.FromSeconds(10));
        after.ExpiresAt.Should().BeGreaterThan(before.ExpiresAt - TimeSpan.FromSeconds(1),
            because: "a refresh extends the backing session by the refresh token lifetime");
    }
}
