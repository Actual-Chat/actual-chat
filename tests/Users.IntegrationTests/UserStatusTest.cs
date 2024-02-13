using ActualChat.App.Server;
using ActualChat.Testing.Host;
using ActualLab.Versioning;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection)), Trait("Category", nameof(UserCollection))]
public class UserStatusTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    private WebClientTester _tester = null!;
    private IAccounts _accounts = null!;
    private AppHost _appHost = null!;
    private Session _adminSession = null!;

    protected override async Task InitializeAsync()
    {
        _appHost = await NewAppHost(options => options with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(("UsersSettings:NewAccountStatus", NewAccountStatus.ToString()));
            },
        });
        _tester = _appHost.NewWebClientTester(Out);
        _accounts = _appHost.Services.GetRequiredService<IAccounts>();
        _adminSession = Session.New();
        await _appHost.SignIn(_adminSession, new User("BobAdmin"));
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldUpdateStatus()
    {
        // arrange
        await _tester.AppHost.SignIn(_adminSession, new User("BobAdmin"));
        await _tester.SignInAsBob();
        var log = _tester.AppServices.LogFor<UserStatusTest>();
        var versionGenerator = _tester.AppServices.GetRequiredService<VersionGenerator<long>>();

        // act
        var account = await GetOwnAccount();

        // assert
        account.Status.Should().Be(NewAccountStatus);

        // act
        var newStatuses = new[] {
            AccountStatus.Inactive, AccountStatus.Suspended,
            AccountStatus.Active, AccountStatus.Inactive,
            AccountStatus.Suspended, AccountStatus.Active,
        };
        foreach (var newStatus in newStatuses) {
            var newAccount = account with { Status = newStatus, Version = versionGenerator.NextVersion(account.Version) };
            log.LogInformation("About to update Status to '{NewStatus}'", newStatus);
            await _tester.Commander.Call(new Accounts_Update(_adminSession, newAccount, account.Version));
            log.LogInformation("Updated Status to '{NewStatus}'", newStatus);

            // assert
            await TestExt.WhenMetAsync(async () => {
                account = await GetOwnAccount();
                log.LogInformation("About to test Status. Expected status: '{ExpectedStatus}', Account: '{Account}'",
                    newStatus, account);
                account.Status.Should().Be(newStatus);
            }, TimeSpan.FromSeconds(3));
        }
    }

    private Task<AccountFull> GetOwnAccount()
        => _accounts.GetOwn(_tester.Session, default);
}
