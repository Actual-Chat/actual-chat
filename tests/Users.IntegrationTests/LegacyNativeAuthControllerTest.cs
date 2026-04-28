using System.Net;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

// Smoke test for the legacy /api/native-auth/sign-in-* HTTP routes that
// pre-RPC MAUI clients still depend on. The shim controller delegates to
// INativeAuth via Commander, so all the security-sensitive logic is
// already covered by AppleSignInEmailSpoofTest — this just verifies
// the route is reachable and ends up signing the session in.

[Collection(nameof(UserCollection))]
public class LegacyNativeAuthControllerTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private AppleTokenEndpointHandlerMock AppleTokenHandler { get; }
        = fixture.AppHost.Services.GetRequiredService<AppleTokenEndpointHandlerMock>();
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();

    [Fact]
    public async Task LegacySignInAppleRouteShouldSignIn()
    {
        // arrange
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session));
        var appleUserId = UniqueNames.AppleId();
        var email = UniqueNames.Email("legacy-apple", "gmail.com");
        var code = AppleTokenHandler.Setup(appleUserId, email);

        // act
        using var client = AppHost.NewHttpClient();
        client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id);
        var response = await client.GetAsync(
            "/api/native-auth/sign-in-apple"
            + $"?userId={Uri.EscapeDataString(appleUserId)}"
            + $"&code={Uri.EscapeDataString(code)}"
            + $"&email={Uri.EscapeDataString(email)}");

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"legacy route should still work. Body: {await response.Content.ReadAsStringAsync()}");

        // First-time sign-in stashes a PendingRegistration prompt — confirm it
        // to commit the account creation, then verify the user is signed in.
        await AppHost.ConfirmPendingRegistration(session);

        var ct = CancellationToken.None;
        var cAccount = await Computed.Capture(() => Accounts.GetOwn(session, ct), ct);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNull(), ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        cAccount.Value.IsGuest.Should().BeFalse();
        cAccount.Value.Email.Should().Be(email);
    }
}
