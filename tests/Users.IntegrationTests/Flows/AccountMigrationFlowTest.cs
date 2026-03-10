using ActualChat.App.Server.Flows;
using ActualChat.Testing.Host;
using ActualChat.Users.Flows;

namespace ActualChat.Users.IntegrationTests.Flows;

public class AccountMigrationFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(AccountMigrationFlowTest)}", TestAppHostOptions.Default, @out)
{
    [Fact]
    public async Task MigrationFlow_Should_Start_AccountMigrationFlow()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        await flowHub.Get<MigrationFlow>(""); // We need to manually start it in this test

        // MigrationFlow should start AccountMigrationFlow
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", ct);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldProcessAccounts()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();

        // Create a test account (non-system user)
        var session = Session.New();
        var testAccount = await h.SignIn(session, TestAuthExt.NewAccount("TestUser"));
        var versionBefore = testAccount.Version;

        // Reset and trigger AccountMigrationFlow to process the newly created account
        await flowHub.NewResumeEvent<AccountMigrationFlow>().WithReset().Schedule();

        // Wait for AccountMigrationFlow to process the account
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            // Flow should have processed at least one account
            flow!.MigratedCount.Should().BeGreaterThan(0);
        }, TimeSpan.FromSeconds(60));

        // Verify the test account version was bumped
        var accountAfter = await accountsBackend.Get(testAccount.Id, CancellationToken.None);
        accountAfter.Should().NotBeNull();
        accountAfter!.Version.Should().BeGreaterThan(versionBefore);
    }

    [Fact]
    public async Task ShouldCompleteWhenAllAccountsProcessed()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        await flowHub.Get<MigrationFlow>("");

        // Wait for AccountMigrationFlow to complete (in test environment with few accounts, it should complete quickly)
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            // Flow should complete with a result when all accounts are processed
            flow!.UntypedResult.Should().NotBeNull();
        }, TimeSpan.FromSeconds(60));
    }
}
