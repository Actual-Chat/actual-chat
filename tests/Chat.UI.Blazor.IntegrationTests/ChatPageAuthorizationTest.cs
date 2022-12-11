using ActualChat.App.Server;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

public class ChatPageAuthorizationTest : AppHostTestBase
{
    private const string ChatId = "the-actual-one";

    private PlaywrightTester _tester = null!;
    private AppHost _appHost = null!;
    private TestSettings _testSettings = null!;
    private IAccounts _accounts = null!;
    private ISessionFactory _sessionFactory = null!;
    private Session _adminSession = null!;

    public ChatPageAuthorizationTest(ITestOutputHelper @out) : base(@out) { }

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost( serverUrls: "http://localhost:7080");
        _testSettings = _appHost.Services.GetRequiredService<TestSettings>();
        _accounts = _appHost.Services.GetRequiredService<IAccounts>();
        _tester = _appHost.NewPlaywrightTester();
        _sessionFactory = _appHost.Services.GetRequiredService<ISessionFactory>();
        _adminSession = _sessionFactory.CreateSession();

        await _tester.AppHost.SignIn(_adminSession, new User("BobAdmin"));
    }

    public override async Task DisposeAsync()
    {
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldNotAuthorizeForInactiveUser()
    {
        // arrange
        var (page, _) = await _tester.NewPage();

        // act
        await page.ClientSignInWithGoogle(_testSettings.User1.Email, _testSettings.User1.Password);
        await UpdateStatus(AccountStatus.Inactive);

        var response = await page.GotoAsync($"/chat/{ChatId}");

        // assert
        response?.Status.Should().Be(StatusCodes.Status200OK);

        var notAuthorizedView = await page.WaitForSelectorAsync("div:text(\"Not authorized\")");
        notAuthorizedView.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldAuthorizeForActiveUser()
    {
        // arrange
        var (page, _) = await _tester.NewPage();

        // act
        await page.ClientSignInWithGoogle(_testSettings.User1.Email, _testSettings.User1.Password);
        await UpdateStatus(AccountStatus.Active);

        var response = await page.GotoAsync($"/chat/{ChatId}");

        // assert
        response?.Status.Should().Be(StatusCodes.Status200OK);

        var noChatFoundView = await page.WaitForSelectorAsync("div:text(\"This chat doesn't exist.\")");
        noChatFoundView.Should().NotBeNull();
    }

    private async Task UpdateStatus(AccountStatus newStatus)
    {
        var account = await _accounts.GetOwn(_tester.Session, default);

        var command = new IAccounts.UpdateCommand(
            _adminSession,
            account with { Status = newStatus },
            account.Version);
        await _accounts.GetCommander().Call(command);
    }
}
