using ActualChat.Host;
using ActualChat.Interception;
using ActualChat.Testing.Host;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Users.IntegrationTests;

public class UserStatusTest : AppHostTestBase
{
    private static readonly UserStatus NewUserStatus = UserStatus.Active;
    private WebClientTester _tester = null!;
    private IUserProfiles _userProfiles = null!;
    private AppHost _appHost = null!;

    public UserStatusTest(ITestOutputHelper @out) : base(@out)
    { }

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost(
            builder => builder.AddInMemory(("UsersSettings:NewUserStatus", NewUserStatus.ToString())));
        _tester = _appHost.NewWebClientTester();
        _userProfiles = _appHost.Services.GetRequiredService<IUserProfiles>();
    }

    public override async Task DisposeAsync()
    {
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldUpdateStatus()
    {
        // arrange
        await _tester.SignIn(new User("", "Bob"));

        // act
        var userProfile = await GetUserProfile();
        var commander = ProxyExt.GetServices(userProfile).Commander();

        // assert
        userProfile.Status.Should().Be(NewUserStatus);

        // act
        foreach (var newStatus in new[] {
                     UserStatus.Inactive, UserStatus.Suspended, UserStatus.Active, UserStatus.Inactive,
                     UserStatus.Suspended, UserStatus.Active,
                 }) {
            userProfile.Status = newStatus;
            await commander.Call(new IUserProfiles.UpdateCommand(_tester.Session, userProfile));

            // assert
            userProfile = await GetUserProfile();
            userProfile.Status.Should().Be(newStatus);
        }
    }

    private async Task<UserProfile> GetUserProfile()
        => await _userProfiles.Get(_tester.Session, default) ?? throw new Exception("User profile not found");
}
