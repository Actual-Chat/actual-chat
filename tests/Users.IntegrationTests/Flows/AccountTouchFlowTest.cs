using ActualChat.Testing.Host;
using ActualChat.Users.Flows;

namespace ActualChat.Users.IntegrationTests.Flows;

public class AccountTouchFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(AccountTouchFlowTest)}", TestAppHostOptions.Default, @out)
{
    [Fact]
    public async Task ShouldStartAccountTouchFlow()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();

        // MasterFlow should start AccountTouchFlow
        await ComputedTest.When(async ct => {
            var masterFlow = await flowHub.TryGet<MasterFlow>("", ct);
            masterFlow.Should().NotBeNull();
            masterFlow!.AppliedMigrations.Contains("StartAccountTouchFlow").Should().BeTrue();
        }, TimeSpan.FromSeconds(30));

        // AccountTouchFlow should exist
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<AccountTouchFlow>("", ct);
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

        // Reset and trigger AccountTouchFlow to process the newly created account
        await flowHub.NewResumeEvent<AccountTouchFlow>().WithReset().Schedule();

        // Wait for AccountTouchFlow to process the account
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<AccountTouchFlow>("", ct);
            flow.Should().NotBeNull();
            // Flow should have processed at least one account
            flow!.TotalProcessed.Should().BeGreaterThan(0);
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

        // Wait for AccountTouchFlow to complete (in test environment with few accounts, it should complete quickly)
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<AccountTouchFlow>("", ct);
            flow.Should().NotBeNull();
            // Flow should complete with a result when all accounts are processed
            flow!.UntypedResult.Should().NotBeNull();
        }, TimeSpan.FromSeconds(60));
    }
}
