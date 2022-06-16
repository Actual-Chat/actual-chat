using System.Security.Claims;
using ActualChat.Host;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;

namespace ActualChat.Users.IntegrationTests;

public class AdminGrantTest : AppHostTestBase
{
    private WebClientTester _tester = null!;
    private IUserProfilesBackend _userProfiles = null!;
    private AppHost _appHost = null!;

    public AdminGrantTest(ITestOutputHelper @out) : base(@out)
    { }

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost();
        _tester = _appHost.NewWebClientTester();
        _userProfiles = _appHost.Services.GetRequiredService<IUserProfilesBackend>();
    }

    public override async Task DisposeAsync()
    {
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task UserWithActualChatDomainAndGoogleIdentityShouldBeGrantedWithAdminRights()
    {
        // arrange
        var user = new User("", "BobAdmin").WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "123"))
            .WithClaim(ClaimTypes.Email, "bob@actual.chat");

        // act
        user = await _tester.SignIn(user);
        var userProfile = await _userProfiles.Get(user.Id, CancellationToken.None);

        // assert
        user.IsAuthenticated.Should().BeTrue();
        userProfile!.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task UserWithoutGoogleIdentityShouldNotBeGrantedWithAdminRights()
    {
        // arrange
        var user = new User("", "JackNotAdmin").WithIdentity(MicrosoftAccountDefaults.AuthenticationScheme)
            .WithClaim(ClaimTypes.Email, "jack@actual.chat");

        // act
        user = await _tester.SignIn(user);
        var userProfile = await _userProfiles.Get(user.Id, CancellationToken.None);

        // assert
        user.IsAuthenticated.Should().BeTrue();
        userProfile!.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task UserWithNotActualChatDomainShouldNotBeGrantedWithAdminRights()
    {
        // arrange
        var user = new User("", "AnnNotAdmin").WithIdentity(GoogleDefaults.AuthenticationScheme);

        // act
        user = await _tester.SignIn(user);
        var userProfile = await _userProfiles.Get(user.Id, CancellationToken.None);

        // assert
        user.IsAuthenticated.Should().BeTrue();
        userProfile!.IsAdmin.Should().BeFalse();
    }
}
