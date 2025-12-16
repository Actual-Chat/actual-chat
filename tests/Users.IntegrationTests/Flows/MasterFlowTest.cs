using ActualChat.Testing.Host;
using ActualChat.Users.Flows;

namespace ActualChat.Users.IntegrationTests.Flows;

public class MasterFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(MasterFlowTest)}", TestAppHostOptions.Default, @out)
{
    [Fact]
    public async Task ShouldStartDigestFlow()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();

        await ComputedTest.When(async ct => {
            var userId = Constants.User.Admin.UserId.Value;
            var flow = await flowHub.TryGet<DigestFlow>(userId, ct);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldBeHangingOnReset()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        await flowHub.Get<MasterFlow>("");

        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<MasterFlow>("", ct);
            flow.Should().NotBeNull();
            flow.AppliedMigrations.Contains("StartDigestFlows").Should().BeTrue();
        }, TimeSpan.FromSeconds(30));
    }
}
