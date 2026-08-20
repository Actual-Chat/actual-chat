using System.Net;
using ActualChat.Security;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

// /maui-auth/start hands the browser a session token cookie, so it accepts only the endpoints
// MauiAccountUI can ask for, and only a navigation the app itself started.

[Collection(nameof(UserCollection))]
public sealed class MauiAuthControllerTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private ISecureTokensBackend SecureTokensBackend
        => AppHost.Services.GetRequiredService<ISecureTokensBackend>();

    [Fact]
    public async Task AllowsTheAppSignInEndpoint()
    {
        // arrange
        var sessionToken = await NewSessionToken();

        // act
        using var client = NewNonRedirectingClient();
        var response = await client.GetAsync(StartUrl(sessionToken, "/signIn/Google"));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/signIn/Google?returnUrl=");
        SetTokenCookies(response).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("/fusion/close?flow=x")]
    [InlineData("/chat/someChatId")]
    [InlineData("//evil.example")]
    [InlineData("/signIn/Google/../../fusion/close?flow=x")]
    public async Task RejectsEndpointsOutsideTheAllowedSet(string endpoint)
    {
        // arrange
        var sessionToken = await NewSessionToken();

        // act
        using var client = NewNonRedirectingClient();
        var response = await client.GetAsync(StartUrl(sessionToken, endpoint));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        SetTokenCookies(response).Should().BeEmpty();
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    [InlineData("same-origin")]
    public async Task RejectsNavigationsStartedByAnotherDocument(string fetchSite)
    {
        // arrange
        var sessionToken = await NewSessionToken();

        // act
        using var client = NewNonRedirectingClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, StartUrl(sessionToken, "/signIn/Google"));
        request.Headers.Add("Sec-Fetch-Site", fetchSite);
        var response = await client.SendAsync(request);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        SetTokenCookies(response).Should().BeEmpty();
    }

    [Fact]
    public async Task AllowsNavigationsTheAppStarted()
    {
        // arrange
        var sessionToken = await NewSessionToken();

        // act
        using var client = NewNonRedirectingClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, StartUrl(sessionToken, "/signIn/Google"));
        request.Headers.Add("Sec-Fetch-Site", "none");
        var response = await client.SendAsync(request);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        SetTokenCookies(response).Should().NotBeEmpty();
    }

    // Private methods

    private async Task<string> NewSessionToken()
    {
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session));
        var token = await SecureTokensBackend.Create(SecureTokenKind.Session, session.Id);

        return token.Token;
    }

    private HttpClient NewNonRedirectingClient()
        => new(new HttpClientHandler { AllowAutoRedirect = false }) {
            BaseAddress = AppHost.Services.UrlMapper().BaseUri,
        };

    private static string StartUrl(string sessionToken, string endpoint)
        => "/maui-auth/start"
            + $"?s={sessionToken.UrlEncode()}"
            + $"&e={endpoint.UrlEncode()}"
            + $"&flow={"Sign-in".UrlEncode()}";

    private static string[] SetTokenCookies(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(x => x.StartsWith(Constants.Session.TokenCookieName)).ToArray()
            : [];
}
