using System.Security.Claims;
using ActualChat.App.Server;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AdminGrantTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IAccountsBackend _accounts = null!;

    protected override Task InitializeAsync()
    {
        _tester = AppHost.NewWebClientTester(Out);
        _accounts = AppHost.Services.GetRequiredService<IAccountsBackend>();
        return Task.CompletedTask;
    }

    protected override Task DisposeAsync()
        => _tester.DisposeAsync().AsTask();

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
