using ActualChat.App.Server;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AccountAutoProvisionTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IAccounts _accounts = null!;
    private AppHost _appHost = null!;

    protected override async Task InitializeAsync()
    {
        _appHost = await NewAppHost();
        _tester = _appHost.NewWebClientTester(Out);
        _accounts = _appHost.Services.GetRequiredService<IAccounts>();
    }

    protected override async Task DisposeAsync()
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
        account.Status.Should().Be(AccountStatus.Active);
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
