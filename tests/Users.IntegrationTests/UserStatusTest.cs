using ActualChat.App.Server;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

public class UserStatusTest : AppHostTestBase
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    private WebClientTester _tester = null!;
    private IAccounts _accounts = null!;
    private AppHost _appHost = null!;
    private ISessionFactory _sessionFactory = null!;
    private Session _adminSession = null!;

    public UserStatusTest(ITestOutputHelper @out) : base(@out)
    { }

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost(
            builder => builder.AddInMemory(("UsersSettings:NewUserStatus", NewAccountStatus.ToString())));
        _tester = _appHost.NewWebClientTester();
        _accounts = _appHost.Services.GetRequiredService<IAccounts>();
        _sessionFactory = _appHost.Services.GetRequiredService<ISessionFactory>();
        _adminSession = _sessionFactory.CreateSession();

        await _tester.AppHost.SignIn(_adminSession, new User("BobAdmin"));
    }

    public override async Task DisposeAsync()
    {
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldUpdateStatus()
    {
        // arrange
        await _tester.AppHost.SignIn(_adminSession, new User("BobAdmin"));
        await _tester.SignIn(new User("Bob"));

        // act
        var account = await RequireAccount();

        // assert
        account.Status.Should().Be(NewAccountStatus);

        // act
        var newStatuses = new[] {
            AccountStatus.Inactive, AccountStatus.Suspended,
            AccountStatus.Active, AccountStatus.Inactive,
            AccountStatus.Suspended, AccountStatus.Active,
        };
        foreach (var newStatus in newStatuses) {
            var newAccount = account with { Status = newStatus };
            await _tester.Commander.Call(new IAccounts.UpdateCommand(_adminSession, newAccount));

            // assert
            account = await RequireAccount();
            account.Status.Should().Be(newStatus);
        }
    }

    private Task<Account> RequireAccount()
        => _accounts.Get(_tester.Session, default).Require();
}
