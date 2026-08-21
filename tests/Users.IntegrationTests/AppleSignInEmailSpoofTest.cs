using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

// Regression tests for Apple Sign-In email spoofing privilege escalation.
// Attack: caller-provided email query param is trusted → spoof @actual.chat → admin.

/// <summary>
/// Tests that Apple Sign-In uses email from Apple's id_token, not from caller-supplied query parameters.
/// </summary>
[Collection(nameof(UserCollection))]
public class AppleSignInEmailSpoofTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private AppleTokenEndpointHandlerMock AppleTokenHandler { get; }
        = fixture.AppHost.Services.GetRequiredService<AppleTokenEndpointHandlerMock>();
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();
    private ISessionTemporalsBackend SessionTemporals => AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();

    [Fact]
    public async Task SpoofedAdminEmailShouldNotGrantAdmin()
    {
        // arrange
        var realEmail = UniqueNames.Email("attacker", "gmail.com");
        var claimedEmail = UniqueNames.Email("attacker");
        var appleUserId = UniqueNames.AppleId();
        var code = AppleTokenHandler.Setup(appleUserId, realEmail);

        // act
        var account = await SignInWithApple(code, appleUserId, claimedEmail);

        // assert
        account.IsGuest.Should().BeFalse("user should be signed in");
        account.IsAdmin.Should().BeFalse(
            "spoofed @actual.chat email in query param must not grant admin; "
            + "email should come from Apple's id_token, not from the caller");
        account.Email.Should().Be(realEmail,
            "persisted email should match id_token, not the caller-supplied query parameter");
        account.Identities.GetEmails().Should().NotContain(claimedEmail,
            "spoofed email should never appear in identity store");
    }

    [Fact]
    public async Task StoredEmailShouldMatchIdTokenNotQueryParam()
    {
        // arrange
        var realEmail = UniqueNames.Email("real-user", "icloud.com");
        var claimedEmail = UniqueNames.Email("impersonated", "example.com");
        var appleUserId = UniqueNames.AppleId();
        var code = AppleTokenHandler.Setup(appleUserId, realEmail);

        // act
        var account = await SignInWithApple(code, appleUserId, claimedEmail);

        // assert
        account.IsGuest.Should().BeFalse("user should be signed in");
        account.Email.Should().Be(realEmail,
            "account email must come from Apple's id_token, not the query parameter");
        account.Identities.GetEmails().Should().Contain(realEmail);
        account.Identities.GetEmails().Should().NotContain(claimedEmail);
    }

    [Fact]
    public async Task AdminEmailInIdTokenShouldGrantAdmin()
    {
        // arrange
        var realEmail = UniqueNames.Email("legit-admin");
        var claimedEmail = UniqueNames.Email("nobody", "gmail.com");
        var appleUserId = UniqueNames.AppleId();
        var code = AppleTokenHandler.Setup(appleUserId, realEmail);

        // act
        var account = await SignInWithApple(code, appleUserId, claimedEmail);

        // assert
        account.IsGuest.Should().BeFalse("user should be signed in");
        account.IsAdmin.Should().BeTrue(
            "legitimate @actual.chat email from Apple's id_token should grant admin");
        account.Email.Should().Be(realEmail);
    }

    [Fact]
    public async Task SpoofedUserIdShouldNotSignInAsVictim()
    {
        // arrange – victim signs in normally, then attacker uses own valid code but spoofs victim's userId
        var victimAppleUserId = UniqueNames.AppleId("apple-victim");
        var attackerAppleUserId = UniqueNames.AppleId("apple-attacker");
        var victimEmail = UniqueNames.Email("victim", "gmail.com");
        var attackerEmail = UniqueNames.Email("attacker", "gmail.com");
        var victimCode = AppleTokenHandler.Setup(victimAppleUserId, victimEmail);
        var attackerCode = AppleTokenHandler.Setup(attackerAppleUserId, attackerEmail);

        // act
        var victimAccount = await SignInWithApple(victimCode, victimAppleUserId, victimEmail);
        var attackerAccount = await SignInWithApple(attackerCode, victimAppleUserId, attackerEmail);

        // assert
        victimAccount.IsGuest.Should().BeFalse();
        attackerAccount.IsGuest.Should().BeFalse();
        attackerAccount.Id.Should().NotBe(victimAccount.Id,
            "attacker must not gain access to victim's account by spoofing userId query param");
        attackerAccount.Identities.HasAppleIdentity(out var appleIdentity).Should().BeTrue();
        appleIdentity.Value.Should().Be(attackerAppleUserId,
            "stored Apple identity must use id_token sub, not the caller-supplied spoofed userId");
    }

    [Fact]
    public async Task SpoofedUserIdAndEmailShouldNotSignInAsVictim()
    {
        // arrange – attacker spoofs both victim's userId and email in query params
        var victimAppleUserId = UniqueNames.AppleId("apple-victim");
        var attackerAppleUserId = UniqueNames.AppleId("apple-attacker");
        var victimEmail = UniqueNames.Email("victim", "gmail.com");
        var attackerEmail = UniqueNames.Email("attacker", "gmail.com");
        var victimCode = AppleTokenHandler.Setup(victimAppleUserId, victimEmail);
        var attackerCode = AppleTokenHandler.Setup(attackerAppleUserId, attackerEmail);

        // act
        var victimAccount = await SignInWithApple(victimCode, victimAppleUserId, victimEmail);
        var attackerAccount = await SignInWithApple(attackerCode, victimAppleUserId, victimEmail);

        // assert
        victimAccount.IsGuest.Should().BeFalse();
        attackerAccount.IsGuest.Should().BeFalse();
        attackerAccount.Id.Should().NotBe(victimAccount.Id,
            "attacker must not gain access to victim's account by spoofing both userId and email");
        attackerAccount.Identities.HasAppleIdentity(out var appleIdentity).Should().BeTrue();
        appleIdentity.Value.Should().Be(attackerAppleUserId,
            "stored Apple identity must use id_token sub, not the caller-supplied spoofed userId");
    }

    // Helpers

    private async Task<AccountFull> SignInWithApple(string code, string queryUserId, string queryEmail)
    {
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session));

        var command = new NativeAuth_SignInApple {
            Session = session,
            UserId = queryUserId,
            Code = code,
            Email = queryEmail,
            Name = null,
        };
        await Commander.Call(command, true, CancellationToken.None);

        var signInError = await SessionTemporals.Get(
            session, Constants.SessionTemporals.SignInErrorKey, CancellationToken.None);
        Out.WriteLine($"SignInError: {signInError}");
        signInError.Should().BeNullOrEmpty("sign-in should not produce errors");

        // First-time Apple sign-in stashes a PendingRegistration prompt instead
        // of auto-creating; the equivalent of the user clicking "Register" in
        // the UI is calling Accounts_ConfirmRegister.
        await AppHost.ConfirmPendingRegistration(session);

        var ct = CancellationToken.None;
        var cAccount = await Computed
            .Capture(() => Accounts.GetOwn(session, ct), ct);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNull(), ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        return cAccount.Value;
    }
}
