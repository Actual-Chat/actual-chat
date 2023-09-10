using ActualChat.App.Server;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

public class AccountAutoProvisionTest : AppHostTestBase
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    private WebClientTester _tester = null!;
    private IAccounts _accounts = null!;
    private AppHost _appHost = null!;

    public AccountAutoProvisionTest(ITestOutputHelper @out) : base(@out)
    { }

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost(
            builder => builder.AddInMemory(("UsersSettings:NewUserStatus", NewAccountStatus.ToString())));
        _tester = _appHost.NewWebClientTester();
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
        var user = await _tester.SignIn(new User("Bob"));

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
        var account = await _tester.SignIn(new User("Bob"));
        await _tester.SignOut();

        // act
        var account2 = await _tester.SignIn(account.User);

        // assert
        account2.Should().BeEquivalentTo(account, options => options
            .Excluding(x => x.User)
            .Excluding(x => x.IsGreetingCompleted ));
    }
}
