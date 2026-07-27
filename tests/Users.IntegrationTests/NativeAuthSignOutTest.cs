using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class NativeAuthSignOutTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private AppleTokenEndpointHandlerMock AppleTokenHandler { get; }
        = fixture.AppHost.Services.GetRequiredService<AppleTokenEndpointHandlerMock>();
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();

    [Fact]
    public async Task SignOutShouldMakeSessionGuest()
    {
        // arrange: sign the session in
        var ct = CancellationToken.None;
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session), ct);
        var appleUserId = UniqueNames.AppleId();
        var email = UniqueNames.Email("native-signout", "gmail.com");
        var code = AppleTokenHandler.Setup(appleUserId, email);
        await Commander.Call(new NativeAuth_SignInApple(session, appleUserId, code, email, "Test User"), ct);
        await AppHost.ConfirmPendingRegistration(session);

        var cAccount = await Computed.Capture(() => Accounts.GetOwn(session, ct), ct);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNull(), ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        cAccount.Value.IsGuest.Should().BeFalse();

        // act
        await Commander.Call(new NativeAuth_SignOut(session), ct);

        // assert
        cAccount = await cAccount
            .When(x => x.IsGuestOrNull(), ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        cAccount.Value.IsGuestOrNull().Should().BeTrue();
    }
}
