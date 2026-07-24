using ActualChat.Testing.Host;
using ActualChat.Users;

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

    // Helpers

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
