using System.Net;
using System.Text;
using ActualChat.Testing;
using ActualChat.Testing.Host;
using ActualChat.Users.Module;
using AspNet.Security.OAuth.Apple;

namespace ActualChat.Users.IntegrationTests;

// Regression tests for Apple Sign-In email spoofing privilege escalation.
// Attack: caller-provided email query param is trusted → spoof @actual.chat → admin.

/// <summary>
/// Tests that Apple Sign-In uses email from Apple's id_token, not from caller-supplied query parameters.
/// </summary>
public class AppleSignInEmailSpoofTest(ITestOutputHelper @out)
    : AppHostTestBase("apple-spoof", TestAppHostOptions.Default, @out)
{
    [Fact]
    public async Task SpoofedAdminEmailShouldNotGrantAdmin()
    {
        // arrange
        var realEmail = "attacker@gmail.com";
        var spoofedEmail = $"attacker{Constants.Team.EmailSuffix}";
        var appleUserId = UniqueNames.AppleId();

        // act
        await using var appHost = await NewAppHost(appleUserId, realEmail);
        var account = await SignInWithApple(appHost, appleUserId, spoofedEmail);

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

        // act
        await using var appHost = await NewAppHost(appleUserId, idTokenEmail);
        var account = await SignInWithApple(appHost, appleUserId, queryEmail);

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

        // act
        await using var appHost = await NewAppHost(appleUserId, adminEmail);
        var account = await SignInWithApple(appHost, appleUserId, queryEmail);

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
        await using var appHost = await NewAppHost(realAppleUserId, email);

        // act
        var account1 = await SignInWithApple(appHost, realAppleUserId, email);
        var account2 = await SignInWithApple(appHost, spoofedAppleUserId, email);

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

    private async Task<TestAppHost> NewAppHost(string idTokenSub, string idTokenEmail)
        => await NewAppHost(options => options with {
            ConfigureHost = (_, cfg) => {
                cfg.AddInMemory<UsersSettings>((x => x.AppleAppId, "com.test.app"));
            },
            ConfigureServices = (_, services) => {
                services.PostConfigure<AppleAuthenticationOptions>(
                    AppleAuthenticationDefaults.AuthenticationScheme,
                    opts => {
                        opts.ClaimsIssuer ??= AppleAuthenticationDefaults.AuthenticationScheme;
                        opts.Backchannel = new HttpClient(
                            new MockAppleTokenEndpointHandler(idTokenSub, idTokenEmail));
                        opts.ClientSecretGenerator = new FakeAppleClientSecretGenerator();
                    });
            },
        });

    private async Task<AccountFull> SignInWithApple(
        TestAppHost appHost,
        string queryUserId,
        string queryEmail)
    {
        var session = Session.New();
        var commander = appHost.Services.Commander();
        await commander.Call(new SessionsBackend_Upsert(session));

        using var client = appHost.NewHttpClient();
        client.DefaultRequestHeaders.Add(Constants.Session.HeaderName, session.Id);
        var response = await client.GetAsync(
            $"/api/native-auth/sign-in-apple"
            + $"?userId={Uri.EscapeDataString(queryUserId)}"
            + $"&code=valid-auth-code"
            + $"&email={Uri.EscapeDataString(queryEmail)}");

        var responseBody = await response.Content.ReadAsStringAsync();
        Out.WriteLine($"Response: {response.StatusCode} - {responseBody}");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"sign-in should succeed (code exchange is mocked). Body: {responseBody}");

        var sessionTemporals = appHost.Services.GetRequiredService<ISessionTemporalsBackend>();
        var signInError = await sessionTemporals.Get(
            session, Constants.SessionTemporals.SignInErrorKey, CancellationToken.None);
        Out.WriteLine($"SignInError: {signInError}");
        signInError.Should().BeNullOrEmpty("sign-in should not produce errors");

        var accounts = appHost.Services.GetRequiredService<IAccounts>();
        var ct = CancellationToken.None;
        var cAccount = await Computed
            .Capture(() => accounts.GetOwn(session, ct), ct);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNull(), ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        return cAccount.Value;
    }

    // Nested types

    /// <summary>
    /// Simulates Apple's token endpoint, returning an id_token JWT with verified sub and email.
    /// </summary>
    private sealed class MockAppleTokenEndpointHandler(string sub, string email) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = Convert.ToBase64String("""{"alg":"none","typ":"JWT"}"""u8)
                .TrimEnd('=');
            var payload = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        $$"""{"sub":"{{sub}}","email":"{{email}}","email_verified":"true"}"""))
                .TrimEnd('=');
            var idToken = $"{header}.{payload}.";

            var json = $$"""
                {
                    "access_token": "mock_access_token",
                    "token_type": "bearer",
                    "expires_in": 3600,
                    "id_token": "{{idToken}}"
                }
                """;

            var response = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Bypasses Apple private key requirements for testing.
    /// </summary>
    private sealed class FakeAppleClientSecretGenerator : AppleClientSecretGenerator
    {
        public override Task<string> GenerateAsync(AppleGenerateClientSecretContext context)
            => Task.FromResult("fake-client-secret");
    }
}
