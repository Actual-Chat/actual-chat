using System.Net;
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
        var realEmail = "attacker@gmail.com";
        var spoofedEmail = $"attacker{Constants.Team.EmailSuffix}";
        var appleUserId = UniqueNames.AppleId();
        var code = AppleTokenHandler.Setup(appleUserId, realEmail);

        // act
        var account = await SignInWithApple(code, appleUserId, spoofedEmail);

        // assert
        account.IsGuest.Should().BeFalse("user should be signed in");
        account.IsAdmin.Should().BeFalse(
            "spoofed @actual.chat email in query param must not grant admin; "
            + "email should come from Apple's id_token, not from the caller");
        account.Email.Should().Be(realEmail,
            "persisted email should match id_token, not the caller-supplied query parameter");
        account.Identities.GetEmails().Should().NotContain(spoofedEmail,
            "spoofed email should never appear in identity store");
    }

    [Fact]
    public async Task StoredEmailShouldMatchIdTokenNotQueryParam()
    {
        // arrange
        var idTokenEmail = "real-user@icloud.com";
        var queryEmail = "impersonated@example.com";
        var appleUserId = UniqueNames.AppleId();
        var code = AppleTokenHandler.Setup(appleUserId, idTokenEmail);

        // act
        var account = await SignInWithApple(code, appleUserId, queryEmail);

        // assert
        account.IsGuest.Should().BeFalse("user should be signed in");
        account.Email.Should().Be(idTokenEmail,
            "account email must come from Apple's id_token, not the query parameter");
        account.Identities.GetEmails().Should().Contain(idTokenEmail);
        account.Identities.GetEmails().Should().NotContain(queryEmail);
    }

    [Fact]
    public async Task AdminEmailInIdTokenShouldGrantAdmin()
    {
        // arrange
        var adminEmail = $"legit-admin{Constants.Team.EmailSuffix}";
        var queryEmail = "nobody@gmail.com";
        var appleUserId = UniqueNames.AppleId();
        var code = AppleTokenHandler.Setup(appleUserId, adminEmail);

        // act
        var account = await SignInWithApple(code, appleUserId, queryEmail);

        // assert
        account.IsGuest.Should().BeFalse("user should be signed in");
        account.IsAdmin.Should().BeTrue(
            "legitimate @actual.chat email from Apple's id_token should grant admin");
        account.Email.Should().Be(adminEmail);
    }

    [Fact]
    public async Task SpoofedUserIdShouldUseIdTokenSub()
    {
        // arrange
        var realAppleUserId = UniqueNames.AppleId("apple-real");
        var spoofedAppleUserId = UniqueNames.AppleId("apple-victim");
        var email = "user@gmail.com";
        var code1 = AppleTokenHandler.Setup(realAppleUserId, email);
        var code2 = AppleTokenHandler.Setup(realAppleUserId, email);

        // act
        var account1 = await SignInWithApple(code1, realAppleUserId, email);
        var account2 = await SignInWithApple(code2, spoofedAppleUserId, email);

        // assert
        account1.IsGuest.Should().BeFalse();
        account2.IsGuest.Should().BeFalse();
        account2.Id.Should().Be(account1.Id,
            "identity should be keyed by id_token sub, not the caller-supplied userId");
        account2.Identities.HasAppleIdentity(out var appleIdentity).Should().BeTrue();
        appleIdentity.Value.Should().Be(realAppleUserId,
            "stored Apple identity must use id_token sub, not the caller-supplied spoofed userId");
    }

    // Helpers

    private async Task<AccountFull> SignInWithApple(string code, string queryUserId, string queryEmail)
    {
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session));

        using var client = AppHost.NewHttpClient();
        client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id);
        var response = await client.GetAsync(
            $"/api/native-auth/sign-in-apple"
            + $"?userId={Uri.EscapeDataString(queryUserId)}"
            + $"&code={Uri.EscapeDataString(code)}"
            + $"&email={Uri.EscapeDataString(queryEmail)}");

        var responseBody = await response.Content.ReadAsStringAsync();
        Out.WriteLine($"Response: {response.StatusCode} - {responseBody}");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"sign-in should succeed (code exchange is mocked). Body: {responseBody}");

        var signInError = await SessionTemporals.Get(
            session, Constants.SessionTemporals.SignInErrorKey, CancellationToken.None);
        Out.WriteLine($"SignInError: {signInError}");
        signInError.Should().BeNullOrEmpty("sign-in should not produce errors");

        var ct = CancellationToken.None;
        var cAccount = await Computed
            .Capture(() => Accounts.GetOwn(session, ct), ct);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNull(), ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        return cAccount.Value;
    }
}
