using System.Net;
using ActualChat.Testing.Host;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth.IntegrationTests;

[Collection(nameof(OAuthCollection))]
public sealed class PrunerTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    private OAuthPruner Pruner => Tester.AppServices.GetRequiredService<OAuthPruner>();

    [Fact]
    public async Task OrphanDcrClientsShouldBePrunedButGrantedOnesKept()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var orphan = await RegisterClient("https://o.example/cb");
        var fresh = await RegisterClient("https://f.example/cb");
        var (granted, _) = await ConnectClient();
        var stale = DateTime.UtcNow - TimeSpan.FromDays(2);
        await Pruner.MarkRegisteredAt(orphan, stale, default);
        await Pruner.MarkRegisteredAt(granted, stale, default);

        // act
        await Pruner.RunOnce(default);

        // assert
        using var scope = Tester.AppServices.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        (await applications.FindByClientIdAsync(orphan)).Should().BeNull(
            because: "a DCR client nobody consented to within DcrPruneAge is an orphan");
        (await applications.FindByClientIdAsync(fresh)).Should().NotBeNull(
            because: "a client registered a moment ago may still be mid-flow");
        (await applications.FindByClientIdAsync(granted)).Should().NotBeNull(
            because: "a client with a grant is in use however old its registration is");
    }

    [Fact]
    public async Task AuthorizationWithDeadSessionShouldBeRevoked()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var authorizationId = ReadJwt(accessToken).Claims.Single(c => c.Type == Claims.Private.AuthorizationId).Value;
        var sid = ReadJwt(accessToken).Claims.Single(c => c.Type == "sid").Value;
        await Tester.Commander.Call(new AccountsBackend_SignOut(new Session(sid), Deactivate: true), true);

        // act
        await Pruner.RunOnce(default);

        // assert
        using var scope = Tester.AppServices.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await authorizations.FindByIdAsync(authorizationId);
        authorization.Should().NotBeNull();
        (await authorizations.GetStatusAsync(authorization!)).Should().Be(Statuses.Revoked,
            because: "a grant whose backing session died can never issue tokens again");
        var (status, body) = await Refresh(clientId, tokens.GetProperty("refresh_token").GetString()!);
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task AuthorizationWithLiveSessionShouldSurvive()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var authorizationId = ReadJwt(tokens.GetProperty("access_token").GetString()!)
            .Claims.Single(c => c.Type == Claims.Private.AuthorizationId).Value;

        // act
        await Pruner.RunOnce(default);

        // assert
        using var scope = Tester.AppServices.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await authorizations.FindByIdAsync(authorizationId);
        (await authorizations.GetStatusAsync(authorization!)).Should().Be(Statuses.Valid);
        var (status, _) = await Refresh(clientId, tokens.GetProperty("refresh_token").GetString()!);
        status.Should().Be(HttpStatusCode.OK, because: "the pruner must leave healthy grants alone");
    }
}
