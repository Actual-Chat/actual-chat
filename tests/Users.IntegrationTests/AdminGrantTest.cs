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
        var accountToSignIn = new AccountFull("BobAdmin")
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "123"))
            .WithClaim(ClaimTypes.Email, $"bob{Constants.Team.EmailSuffix}");

        // act
        var account = await _tester.SignIn(accountToSignIn);

        // assert
        account.Should().NotBeNull();
        account.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task UserWithoutGoogleIdentityShouldNotBeGrantedWithAdminRights()
    {
        // arrange
        var accountToSignIn = new AccountFull("JackNotAdmin")
            .WithIdentity(new UserIdentity(MicrosoftAccountDefaults.AuthenticationScheme, Ulid.NewUlid().ToString()))
            .WithClaim(ClaimTypes.Email, $"jack{Constants.Team.EmailSuffix}");

        // act
        var account = await _tester.SignIn(accountToSignIn);

        // assert
        account.Should().NotBeNull();
        account.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task UserWithNotActualChatDomainShouldNotBeGrantedWithAdminRights()
    {
        // arrange
        var accountToSignIn = new AccountFull("AnnNotAdmin")
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, Ulid.NewUlid().ToString()));

        // act
        var account = await _tester.SignIn(accountToSignIn);

        // assert
        account.Should().NotBeNull();
        account.IsAdmin.Should().BeFalse();
    }
}
