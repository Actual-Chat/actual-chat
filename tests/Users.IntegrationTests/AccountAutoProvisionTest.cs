using ActualChat.App.Server;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection)), Trait("Category", nameof(UserCollection))]
public class AccountAutoProvisionTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    private WebClientTester _tester = null!;
    private IAccounts _accounts = null!;
    private AppHost _appHost = null!;

    public override async Task InitializeAsync()
    {
        _appHost = await Fixture.NewHost(options => options with {
            Output = Out,
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(("UsersSettings:NewAccountStatus", NewAccountStatus.ToString()));
            },
        });
        _tester = _appHost.NewWebClientTester(Out);
        _accounts = _appHost.Services.GetRequiredService<IAccounts>();
    }

    public override async Task DisposeAsync()
    {
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldCreateAccountForNewUser()
    {
        // arrange
        var user = await _tester.SignInAsBob();

        // act
        var account = await _accounts.GetOwn(_tester.Session, default);

        // assert
        account.Should().NotBeNull();
        account.Id.Should().Be(user.Id);
        account.Status.Should().Be(NewAccountStatus);
    }

    [Fact]
    public async Task ShouldNotCreateAccountForExistingUser()
    {
        // arrange
        var account = await _tester.SignInAsBob();
        await _tester.SignOut();

        // act
        var account2 = await _tester.SignIn(account.User);

        // assert
        account2.Should().BeEquivalentTo(account, options => options
            .Excluding(x => x.User)
            .Excluding(x => x.IsGreetingCompleted ));
    }
}
