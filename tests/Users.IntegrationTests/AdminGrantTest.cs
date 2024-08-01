using System.Security.Claims;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AdminGrantTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task UserWithActualChatDomainAndGoogleIdentityShouldBeGrantedWithAdminRights()
    {
        // arrange
        var user = new User("", "BobAdmin")
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "123"))
            .WithClaim(ClaimTypes.Email, "bob@actual.chat");

        // act
        var account = await _tester.SignIn(user);

        // assert
        user.Should().NotBeNull();
        account.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task UserWithoutGoogleIdentityShouldNotBeGrantedWithAdminRights()
    {
        // arrange
        var user = new User("", "JackNotAdmin")
            .WithIdentity(MicrosoftAccountDefaults.AuthenticationScheme)
            .WithClaim(ClaimTypes.Email, "jack@actual.chat");

        // act
        var account = await _tester.SignIn(user);

        // assert
        user.Should().NotBeNull();
        account.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task UserWithNotActualChatDomainShouldNotBeGrantedWithAdminRights()
    {
        // arrange
        var user = new User("", "AnnNotAdmin").WithIdentity(GoogleDefaults.AuthenticationScheme);

        // act
        var account = await _tester.SignIn(user);

        // assert
        user.Should().NotBeNull();
        account.IsAdmin.Should().BeFalse();
    }
}
