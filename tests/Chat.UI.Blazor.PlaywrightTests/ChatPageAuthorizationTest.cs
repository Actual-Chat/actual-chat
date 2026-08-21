using ActualChat.Testing.Host;
using Microsoft.AspNetCore.Http;

namespace ActualChat.UI.Blazor.App.PlaywrightTests;

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
        await _tester.AppHost.SignIn(_adminSession, new AccountFull("BobAdmin"));
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact(Skip = "2025.02+ AppHost doesn't serve web pages.")]
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

    [Fact(Skip = "2025.02+ AppHost doesn't serve web pages.")]
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

        var command = new Accounts_Update {
            Session = _adminSession,
            Account = account with { Status = newStatus },
            ExpectedVersion = account.Version,
        };
        await _accounts.GetCommander().Call(command);
    }
}
