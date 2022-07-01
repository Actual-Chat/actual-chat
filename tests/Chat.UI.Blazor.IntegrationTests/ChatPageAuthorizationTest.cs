using ActualChat.Host;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Http;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

public class ChatPageAuthorizationTest : AppHostTestBase
{
    private const string ChatId = "the-actual-one";

    private PlaywrightTester _tester = null!;
    private AppHost _appHost = null!;
    private TestSettings _testSettings = null!;
    private IUserProfiles _userProfiles = null!;
    private ISessionFactory _sessionFactory = null!;
    private Session _adminSession = null!;

    public ChatPageAuthorizationTest(ITestOutputHelper @out) : base(@out) { }

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost( serverUrls: "http://localhost:7080");
        _testSettings = _appHost.Services.GetRequiredService<TestSettings>();
        _userProfiles = _appHost.Services.GetRequiredService<IUserProfiles>();
        _tester = _appHost.NewPlaywrightTester();
        _sessionFactory = _appHost.Services.GetRequiredService<ISessionFactory>();
        _adminSession = _sessionFactory.CreateSession();

        await _tester.AppHost.SignIn(_adminSession, new User("", "BobAdmin"));
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
        await UpdateStatus(UserStatus.Inactive);

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
        await UpdateStatus(UserStatus.Active);

        var response = await page.GotoAsync($"/chat/{ChatId}");

        // assert
        response?.Status.Should().Be(StatusCodes.Status200OK);

        var noChatFoundView = await page.WaitForSelectorAsync("div:text(\"This chat doesn't exist.\")");
        noChatFoundView.Should().NotBeNull();
    }

    private async Task UpdateStatus(UserStatus newStatus)
    {
        var userProfile = await _userProfiles.Get(_tester.Session, default);
        userProfile!.Status = newStatus;
        var commander = ProxyExt.GetServices(userProfile).Commander();
        await commander.Call(new IUserProfiles.UpdateCommand(_adminSession, userProfile));
    }
}
