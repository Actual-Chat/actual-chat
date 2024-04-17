using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
[SuppressMessage("Usage", "xUnit1041:Fixture arguments to test classes must have fixture sources")]
public class UserStatusTest(AppHostFixture fixture, ITestOutputHelper @out, ILogger<UserStatusTest> log)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out, log)
{
    private WebClientTester _tester = null!;
    private IAccounts _accounts = null!;
    private Session _adminSession = null!;

    protected override async Task InitializeAsync()
    {
        _tester = AppHost.NewWebClientTester(Out);
        _accounts = AppHost.Services.GetRequiredService<IAccounts>();
        _adminSession = Session.New();
        await AppHost.SignIn(_adminSession, UserOperations.NewAdmin());
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldUpdateStatus()
    {
        // arrange
        await _tester.SignInAsAlice();

        // act
        var account = await _accounts.GetOwn(_tester.Session, default);

        // assert
        account.Status.Should().Be(AccountStatus.Active);

        // act
        var newStatuses = new[] {
            AccountStatus.Inactive, AccountStatus.Suspended,
            AccountStatus.Active, AccountStatus.Inactive,
            AccountStatus.Suspended, AccountStatus.Active,
        };
        foreach (var newStatus in newStatuses) {
            var updatedAccount = account with { Status = newStatus };
            Log.LogInformation("About to update Status to '{NewStatus}'", newStatus);
            // intentionally pass expectedVersion=null to avoid version mismatch while greeting process changes account
            await Commander.Call(new Accounts_Update(_adminSession, updatedAccount, null));
            Log.LogInformation("Updated Status to '{NewStatus}'", newStatus);

            // assert
            await ComputedTestExt.When(AppHost.Services, async ct => {
                account = await _accounts.GetOwn(_tester.Session, ct);
                account.Status.Should().Be(newStatus);
            });
        }
    }
}
