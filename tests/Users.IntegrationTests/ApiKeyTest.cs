using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
[SuppressMessage("Usage", "xUnit1041:Fixture arguments to test classes must have fixture sources")]
public class ApiKeyTest(AppHostFixture fixture, ITestOutputHelper @out, ILogger<ApiKeyTest> log)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out, log)
{
    private WebClientTester _tester = null!;
    private IAccounts _accounts = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        _accounts = AppHost.Services.GetRequiredService<IAccounts>();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task CreateApiKey_ShouldUpdateListOwnSessions()
    {
        // Arrange
        await _tester.SignInAsUniqueAlice();
        var session = _tester.Session;

        // Verify no API keys initially
        var apiKeys = await _accounts.ListOwnSessions(session, SessionKind.ApiKey, default);
        apiKeys.Count.Should().Be(0);

        // Act — create an API key
        var createCommand = new Accounts_CreateApiKey { Session = session, Name = "Test Key", ExpiresInDays = 30 };
        var apiKeyId = await Commander.Call(createCommand, default);
        apiKeyId.Should().NotBeNullOrEmpty();
        apiKeyId[0].Should().Be(CoreConstants.Session.ApiKeyPrefix);

        // Assert — ListOwnSessions should now include the new key
        await ComputedTest.When(async ct => {
            var keys = await _accounts.ListOwnSessions(session, SessionKind.ApiKey, ct);
            keys.Count.Should().Be(1);
            keys[0].Description.Should().Be("Test Key");
            keys[0].IsActive.Should().BeTrue();
        });
    }

    [Fact]
    public async Task DeactivateApiKey_ShouldUpdateListOwnSessions()
    {
        // Arrange
        await _tester.SignInAsUniqueBob();
        var session = _tester.Session;

        // Create an API key
        var createCommand = new Accounts_CreateApiKey {
            Session = session,
            Name = "Key To Deactivate",
            ExpiresInDays = 30,
        };
        var apiKeyId = await Commander.Call(createCommand, default);

        // Wait for it to appear
        await ComputedTest.When(async ct => {
            var keys = await _accounts.ListOwnSessions(session, SessionKind.ApiKey, ct);
            keys.Count.Should().Be(1);
        });

        // Act — deactivate it
        var apiKeySession = new Session(apiKeyId);
        var deactivateCommand = new Accounts_DeactivateSession { Session = session, IdPrefix = apiKeySession.IdPrefix };
        await Commander.Call(deactivateCommand, default);

        // Assert — should disappear from active list
        await ComputedTest.When(async ct => {
            var keys = await _accounts.ListOwnSessions(session, SessionKind.ApiKey, ct);
            keys.Count.Should().Be(0); // Inactive keys are filtered out
        });
    }

    [Fact]
    public async Task CreateApiKey_ShouldNotAffectSessionsList()
    {
        // Arrange
        await _tester.SignInAsNew("Charlie");
        var session = _tester.Session;

        // Check initial sessions count
        var sessionsBefore = await _accounts.ListOwnSessions(session, SessionKind.Session, default);

        // Act — create an API key
        var createCommand = new Accounts_CreateApiKey { Session = session, Name = "My Key", ExpiresInDays = 365 };
        await Commander.Call(createCommand, default);

        // Assert — sessions list should not change
        var sessionsAfter = await _accounts.ListOwnSessions(session, SessionKind.Session, default);
        sessionsAfter.Count.Should().Be(sessionsBefore.Count);

        // But API keys list should have one entry
        await ComputedTest.When(async ct => {
            var keys = await _accounts.ListOwnSessions(session, SessionKind.ApiKey, ct);
            keys.Count.Should().Be(1);
        });
    }

    [Fact]
    public async Task ApiKey_ShouldAuthenticateAsOwner()
    {
        // Arrange — sign in and create an API key
        await _tester.SignInAsNew("Dave");
        var session = _tester.Session;
        var ownAccount = await _accounts.GetOwn(session, default);

        var createCommand = new Accounts_CreateApiKey { Session = session, Name = "Auth Test Key", ExpiresInDays = 30 };
        var apiKeyId = await Commander.Call(createCommand, default);

        // Act — use the API key session to get own account
        var apiKeySession = new Session(apiKeyId);
        var accountViaApiKey = await _accounts.GetOwn(apiKeySession, default);

        // Assert — should return the same account
        accountViaApiKey.Id.Should().Be(ownAccount.Id);
        accountViaApiKey.Name.Should().Be(ownAccount.Name);
    }
}
