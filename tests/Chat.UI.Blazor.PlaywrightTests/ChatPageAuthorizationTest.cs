using ActualChat.App.Server;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Chat.UI.Blazor.PlaywrightTests;

[Collection(nameof(ChatUIAutomationCollection))]
public class ChatPageAuthorizationTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string ChatId = "the-actual-one";

    private PlaywrightTester _tester = null!;
    private TestSettings _testSettings = null!;
    private IAccounts _accounts = null!;
    private Session _adminSession = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _testSettings = AppHost.Services.GetRequiredService<TestSettings>();
        _accounts = AppHost.Services.GetRequiredService<IAccounts>();
        _tester = AppHost.NewPlaywrightTester(Out);
        _adminSession = Session.New();
        await _tester.AppHost.SignIn(_adminSession, new User("BobAdmin"));
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
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

        var command = new Accounts_Update(
            _adminSession,
            account with { Status = newStatus },
            account.Version);
        await _accounts.GetCommander().Call(command);
    }
}
