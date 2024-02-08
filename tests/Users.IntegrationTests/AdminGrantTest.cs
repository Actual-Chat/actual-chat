using System.Security.Claims;
using ActualChat.App.Server;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection)), Trait("Category", nameof(UserCollection))]
public class AdminGrantTest(AppHostFixture fixture, ITestOutputHelper @out): IAsyncLifetime
{
    private TestAppHost Host => fixture.Host;
    private ITestOutputHelper Out { get; } = fixture.Host.UseOutput(@out);

    private WebClientTester _tester = null!;
    private IAccountsBackend _accounts = null!;

    public Task InitializeAsync()
    {
        _tester = Host.NewWebClientTester(Out);
        _accounts = Host.Services.GetRequiredService<IAccountsBackend>();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
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
