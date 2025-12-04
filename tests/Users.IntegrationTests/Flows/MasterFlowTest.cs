using ActualChat.Flows;
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

        var flows = h.Services.GetRequiredService<IFlows>();

        await ComputedTest.When(async ct => {
            var userId = Constants.User.Admin.UserId.Value;
            var flow = await flows.TryGet<DigestFlow>(userId, ct);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldBeHangingOnReset()
    {
        await using var h = await NewAppHost();

        var flows = h.Services.GetRequiredService<IFlows>();
        await flows.Get<MasterFlow>("");

        await ComputedTest.When(async ct => {
            var flow = await flows.TryGet<MasterFlow>("", ct);
            flow!.Step.Should().Be("OnReset");
            flow.HardResumeAt!.Value.ToDateTime().Year.Should().Be(2100);
        }, TimeSpan.FromSeconds(30));
    }
}
