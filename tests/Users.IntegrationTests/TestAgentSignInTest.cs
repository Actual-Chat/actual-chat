using ActualChat.Hosting;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualChat.Users.Email;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class TestAgentSignInTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task TestAgentShouldNotBeAdmin()
    {
        var (account, _) = await SignInAsTestAgent();
        account.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public void TestAgentShouldBeAdminOnLocalHostsOnly()
    {
        // arrange
        var email = ActualChat.Email.Parse($"test-{Ulid.NewUlid().ToString().ToLower()}@actual.chat");
        var account = new AccountFull("").WithEmailIdentity(email);
        var predefinedTotps = ImmutableDictionary<string, int>.Empty;

        // act
        var isAdminOnLocal = AccountsBackend.IsAdmin(account, true, predefinedTotps);
        var isAdminElsewhere = AccountsBackend.IsAdmin(account, false, predefinedTotps);

        // assert
        isAdminOnLocal.Should().BeTrue();
        isAdminElsewhere.Should().BeFalse();
    }

    [Fact]
    public async Task TestAgentShouldHaveVerifiedEmail()
    {
        var (account, email) = await SignInAsTestAgent();
        account.IsEmailVerified().Should().BeTrue();
        account.Email.Should().Be(email);
    }

    [Fact]
    public async Task TestAgentShouldHaveEmailIdentity()
    {
        var (account, email) = await SignInAsTestAgent();
        account.Identities.GetEmails().Should().Contain(email);
        account.Identities.GetEmailIdentities().Should().NotBeEmpty();
    }

    [Fact]
    public async Task TestAgentShouldHaveEarlyAccessEnabled()
    {
        var (account, _) = await SignInAsTestAgent();

        var serverKvasBackend = AppHost.Services.GetRequiredService<IServerKvasBackend>();
        var kvas = serverKvasBackend.ForUser(account.Id);
        var appSettings = await kvas.UserAppSettings().Get(default);
        appSettings.AreExperimentalFeaturesEnabled.Should().BeTrue();
        appSettings.IsIncompleteUIEnabled.Should().BeTrue();
    }

    [Fact]
    public void RefusesConstantCodeOnUnknownHost()
    {
        // arrange
        var hostInfo = new HostInfo {
            BaseUrl = "https://preview.example.com",
            IsTested = false,
        };
        var localHostInfo = hostInfo with { BaseUrl = "https://local.voxt.ai" };

        // act
        var isAllowed = EmailAuth.IsTestAgentEmailTotpHost(hostInfo);
        var isLocalAllowed = EmailAuth.IsTestAgentEmailTotpHost(localHostInfo);

        // assert
        isAllowed.Should().BeFalse();
        isLocalAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task EmailValidationMessageKeepsLegacyRoutes()
    {
        // arrange
        var emailAuth = AppHost.Services.GetRequiredService<IEmailAuth>();
        var email = ActualChat.Email.Parse($"test-{Ulid.NewUlid().ToString().ToLower()}@actual.chat");
        var arguments = new object[] {
            _tester.Session,
            email,
            TotpPurpose.SignInEmail,
            CancellationToken.None,
        };
        var routeNames = new[] {
            "GetEmailValidationMessage",
            "CheckIfBlocked",
        };

        // act
        var methods = routeNames.Select(x => typeof(IEmailAuth).GetMethod(x)).ToArray();
        var results = new List<string>();
        foreach (var method in methods) {
            if (method is null)
                continue;

            var task = (Task<string>)method.Invoke(emailAuth, arguments)!;
            results.Add(await task);
        }

        // assert
        methods.Should().AllSatisfy(x => x.Should().NotBeNull());
        results.Should().Equal(string.Empty, string.Empty);
    }

    // Private methods

    private async Task<(AccountFull Account, string Email)> SignInAsTestAgent()
    {
        var emailString = $"test-{Ulid.NewUlid().ToString().ToLower()}@actual.chat";
        var email = ActualChat.Email.Parse(emailString);
        var session = _tester.Session;

        var sendCmd = new EmailAuth_SendTotp(session, email);
        await _tester.Commander.Call(sendCmd);

        var validateCmd = new EmailAuth_ValidateTotp(session, email, 111111);
        var success = await _tester.Commander.Call(validateCmd);
        success.Should().BeTrue();

        // First-time TOTP sign-in stashes a PendingRegistration prompt; emulate
        // the user clicking "Register" in the confirmation modal.
        await AppHost.ConfirmPendingRegistration(session);

        var account = await _tester.Accounts.GetOwn(session, default);
        account.Should().NotBeNull();
        return (account, emailString);
    }
}
