using System.Net;
using ActualChat.OAuth;
using ActualChat.Testing.Host;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(OAuthCollection))]
public sealed class RevocationTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    private IOAuthGrants Grants => Tester.AppServices.GetRequiredService<IOAuthGrants>();
    private ISessionsBackend SessionsBackend => Tester.AppServices.GetRequiredService<ISessionsBackend>();

    [Fact]
    public async Task RevokingRefreshTokenShouldDeactivateSession()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var backingSession = new Session(ReadJwt(accessToken).Claims.Single(c => c.Type == "sid").Value);

        // act
        var response = await Revoke(clientId, tokens.GetProperty("refresh_token").GetString()!);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionInfo = await SessionsBackend.Get(backingSession, default);
        sessionInfo.Should().NotBeNull();
        sessionInfo!.IsActive.Should().BeFalse(because: "the last refresh token is gone, so the grant is dead");
        (await SendInitialize("Bearer " + accessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ComputedTest.When(async ct => {
            var grants = await Grants.List(Tester.Session, ct);
            grants.Should().NotContain(g => g.ClientId == clientId, because: "a dead grant must leave the list");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RevokingAccessTokenShouldKeepGrant()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var backingSession = new Session(ReadJwt(accessToken).Claims.Single(c => c.Type == "sid").Value);

        // act
        var response = await Revoke(clientId, accessToken);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionInfo = await SessionsBackend.Get(backingSession, default);
        sessionInfo!.IsActive.Should().BeTrue(because: "the refresh token is still valid, so the client can come back");
        var (status, _) = await Refresh(clientId, tokens.GetProperty("refresh_token").GetString()!);
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokingRedeemedRefreshTokenShouldDropGrant()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var oldRefreshToken = tokens.GetProperty("refresh_token").GetString()!;
        var (_, refreshed) = await Refresh(clientId, oldRefreshToken);
        var newRefreshToken = refreshed.GetProperty("refresh_token").GetString()!;
        var backingSession = new Session(
            ReadJwt(refreshed.GetProperty("access_token").GetString()!).Claims.Single(c => c.Type == "sid").Value);

        // act
        var response = await Revoke(clientId, oldRefreshToken);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: "RFC 7009 hides token state from the caller");
        using var scope = Tester.AppServices.CreateScope();
        var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var current = await tokenManager.FindByReferenceIdAsync(newRefreshToken);
        current.Should().NotBeNull();
        (await tokenManager.GetStatusAsync(current!)).Should().Be(Statuses.Revoked,
            because: "presenting a redeemed refresh token is reuse, which revokes the whole grant's tokens");
        var sessionInfo = await SessionsBackend.Get(backingSession, default);
        sessionInfo!.IsActive.Should().BeFalse(because: "no valid refresh token is left, so the grant is dead");
    }

    // Private methods

    private Task<HttpResponseMessage> Revoke(string clientId, string token)
        => Http.PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["token"] = token,
            ["client_id"] = clientId,
        }));
}
