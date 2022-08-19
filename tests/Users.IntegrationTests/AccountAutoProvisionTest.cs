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
        var account = (await _accounts.Get(_tester.Session, default))!;

        // assert
        account.Should().NotBeNull();
        account.Id.Should().Be(user.Id);
        account.Status.Should().Be(NewAccountStatus);
    }

    [Fact]
    public async Task ShouldNotTouchAccountForExistingUser()
    {
        // arrange
        var user = await _tester.SignIn(new User("Bob"));
        var expected = (await _accounts.Get(_tester.Session, default))!;
        await _tester.SignOut();

        // act
        await _tester.SignIn(user);
        var actual = (await _accounts.Get(_tester.Session, default))!;

        // assert
        actual.Should().BeEquivalentTo(expected, options => options.Excluding(x => x.User));
    }
}
