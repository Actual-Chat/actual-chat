using ActualChat.Host;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

public class ChatPageAuthorizationTest : AppHostTestBase
{
    private PlaywrightTester _tester = null!;
    private AppHost _appHost = null!;
    private IUserProfiles _userProfiles = null!;
    private ISessionFactory _sessionFactory = null!;
    private IOptions<TestUsersOptions> TestUsers { get; set; } = null!;
    private const string ChatId = "the-actual-one";

    private Session AdminSession { get; set; } = null!;

    public ChatPageAuthorizationTest(ITestOutputHelper @out) : base(@out) { }

    public override async Task InitializeAsync()
    {
        _appHost = await TestHostFactory.NewAppHost(serverUrls: "http://localhost:7080");
        _userProfiles = _appHost.Services.GetRequiredService<IUserProfiles>();
        _tester = _appHost.NewPlaywrightTester();
        _sessionFactory = _appHost.Services.GetRequiredService<ISessionFactory>();

        TestUsers = _appHost.Services.GetRequiredService<IOptions<TestUsersOptions>>();
        AdminSession = _sessionFactory.CreateSession();
        await _tester.AppHost.SignIn(AdminSession, new User("", "BobAdmin"));
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
        await page.ClientSignInWithGoogle(TestUsers.Value.User1.Email, TestUsers.Value.User1.Password);
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
        await page.ClientSignInWithGoogle(TestUsers.Value.User1.Email, TestUsers.Value.User1.Password);
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
        await _userProfiles.UpdateStatus(
            new IUserProfiles.UpdateStatusCommand(userProfile!.Id, newStatus, AdminSession),
            default);
    }
}
