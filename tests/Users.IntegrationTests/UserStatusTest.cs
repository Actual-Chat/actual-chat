using ActualChat.Host;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

public class UserStatusTest : AppHostTestBase
{
    private static readonly UserStatus NewUserStatus = UserStatus.Active;
    private WebClientTester _tester = null!;
    private IUserProfiles _userProfiles = null!;
    private AppHost _appHost = null!;
    private ISessionFactory _sessionFactory = null!;
    private Session _adminSession = null!;

    public UserStatusTest(ITestOutputHelper @out) : base(@out)
    { }

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost(
            builder => builder.AddInMemory(("UsersSettings:NewUserStatus", NewUserStatus.ToString())));
        _tester = _appHost.NewWebClientTester();
        _userProfiles = _appHost.Services.GetRequiredService<IUserProfiles>();
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
    public async Task ShouldUpdateStatus()
    {
        // arrange
        await _tester.AppHost.SignIn(_adminSession, new User("BobAdmin"));
        await _tester.SignIn(new User("Bob"));

        // act
        var userProfile = await RequireUserProfile();

        // assert
        userProfile.Status.Should().Be(NewUserStatus);

        // act
        var newStatuses = new[] {
            UserStatus.Inactive, UserStatus.Suspended,
            UserStatus.Active, UserStatus.Inactive,
            UserStatus.Suspended, UserStatus.Active,
        };
        foreach (var newStatus in newStatuses) {
            var newUserProfile = userProfile with { Status = newStatus };
            await _tester.Commander.Call(new IUserProfiles.UpdateCommand(_adminSession, newUserProfile));

            // assert
            userProfile = await RequireUserProfile();
            userProfile.Status.Should().Be(newStatus);
        }
    }

    private Task<UserProfile> RequireUserProfile()
        => _userProfiles.Get(_tester.Session, default).Require();
}
