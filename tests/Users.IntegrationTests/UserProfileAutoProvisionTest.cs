using ActualChat.Host;
using ActualChat.Testing.Host;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Users.IntegrationTests;

public class UserProfileAutoProvisionTest : AppHostTestBase
{
    private static readonly UserStatus NewUserStatus = UserStatus.Active;
    private WebClientTester _tester = null!;
    private IUserProfiles _userProfiles = null!;
    private AppHost _appHost = null!;

    public UserProfileAutoProvisionTest(ITestOutputHelper @out) : base(@out)
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
    public async Task ShouldCreateProfileForNewUser()
    {
        // arrange
        var user = await _tester.SignIn(new User("", "Bob"));

        // act
        var userProfile = (await _userProfiles.Get(_tester.Session, default))!;

        // assert
        userProfile.Should().NotBeNull();
        userProfile.Id.Should().Be(user.Id);
        userProfile.Status.Should().Be(NewUserStatus);
    }

    [Fact]
    public async Task ShouldNotTouchProfileForExistingUser()
    {
        // arrange
        var user = await _tester.SignIn(new User("", "Bob"));
        var expected = (await _userProfiles.Get(_tester.Session, default))!;
        await _tester.SignOut();

        // act
        await _tester.SignIn(user);
        var actual = (await _userProfiles.Get(_tester.Session, default))!;

        // assert
        actual.Should().BeEquivalentTo(expected, options => options.Excluding(x => x.User));
    }
}
