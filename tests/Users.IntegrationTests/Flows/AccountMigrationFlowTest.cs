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
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        await flowHub.Get<MigrationFlow>("", ct); // We need to manually start it in this test

        // MigrationFlow should start AccountMigrationFlow
        await ComputedTest.When(async innerCt => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", innerCt);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldProcessAccounts()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();

        // Create a test account (non-system user)
        var session = Session.New();
        await h.SignIn(session, TestAuthExt.NewAccount("TestUser"));

        // Reset and trigger AccountMigrationFlow to process the newly created account
        await flowHub.NewResumeEvent<AccountMigrationFlow>().WithReset().Schedule(ct);

        // Wait for AccountMigrationFlow to process the account
        await ComputedTest.When(async innerCt => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", innerCt);
            flow.Should().NotBeNull();
            // Flow should have processed at least one account
            flow!.MigratedCount.Should().BeGreaterThan(0);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task ShouldCompleteWhenAllAccountsProcessed()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        await flowHub.Get<MigrationFlow>("", ct);

        // Wait for AccountMigrationFlow to complete (in test environment with few accounts, it should complete quickly)
        await ComputedTest.When(async innerCt => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", innerCt);
            flow.Should().NotBeNull();
            // Flow should complete with a result when all accounts are processed
            flow!.UntypedResult.Should().NotBeNull();
        }, TimeSpan.FromSeconds(60));
    }
}
