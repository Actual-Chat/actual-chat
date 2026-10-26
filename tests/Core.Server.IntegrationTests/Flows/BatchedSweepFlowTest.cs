using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Testing.Host;
using ActualLab.CommandR.Operations;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Trait("Category", "Slow")]
public sealed class BatchedSweepFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(BatchedSweepFlowTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.AddFlows(useMasterFlows: false)
            .Add<SimpleBatchedSweepFlow>()
            .Add<QuantaFlow>(),
    }, @out)
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(15);

    [Fact(Timeout = 120_000)]
    public async Task ExternalResumeShouldStartASweepThatCompletes()
    {
        // arrange
        await using var h = await NewAppHost();
        var flowHub = h.Services.FlowHub();
        var args = $"{RandomStringGenerator.Default.Next()},3";

        // act
        await flowHub.NewResumeEvent<SimpleBatchedSweepFlow>(args).Schedule();

        // assert
        await TestWait.When(async ct => {
            var flow = await flowHub.TryGet<SimpleBatchedSweepFlow>(args, ct);
            flow.Should().NotBeNull("the resume must create the flow");
            flow!.SweepCount.Should().Be(1, "one external resume asks for one sweep");
            flow.IsSweeping.Should().BeFalse("the sweep is over");
            flow.TotalBatchCount.Should().Be(3, "the sweep runs every batch once");
        }, WaitBudget);
    }

    [Fact(Timeout = 120_000)]
    public async Task ResumesDuringASweepShouldEarnOneMoreSweepAndNoExtraBatches()
    {
        // arrange
        await using var h = await NewAppHost();
        var flowHub = h.Services.FlowHub();
        var args = $"{RandomStringGenerator.Default.Next()},5";
        await flowHub.NewResumeEvent<SimpleBatchedSweepFlow>(args).Schedule();
        await TestWait.When(async ct => {
            var flow = await flowHub.TryGet<SimpleBatchedSweepFlow>(args, ct);
            flow.Should().NotBeNull("the resume must create the flow");
            flow!.IsSweeping.Should().BeTrue("the first sweep must be under way");
        }, WaitBudget);

        // act
        for (var i = 0; i < 3; i++)
            await flowHub.NewResumeEvent<SimpleBatchedSweepFlow>(args).Schedule();

        // assert
        await TestWait.When(async ct => {
            var flow = await flowHub.TryGet<SimpleBatchedSweepFlow>(args, ct);
            flow!.SweepCount.Should().Be(2, "resumes during a sweep fold into one more sweep");
            flow.IsSweeping.Should().BeFalse("the second sweep is over");
            flow.TotalBatchCount.Should().Be(10, "an external resume during a sweep runs no batch of its own");
        }, WaitBudget);
    }

    [Fact(Timeout = 120_000)]
    public async Task SelfAndExternalResumesInOneSlotShouldGetDistinctUuids()
    {
        // arrange
        await using var h = await NewAppHost();
        var flowHub = h.Services.FlowHub();
        var flowId = flowHub.NewId<QuantaFlow>("uuids,self");
        var delayUntil = flowHub.SystemNow + TimeSpan.FromMinutes(1);
        var external = flowHub.NewResumeEvent(flowId).WithDelay(delayUntil);
        var self = flowHub.NewResumeEvent(flowId).WithDelay(delayUntil).WithSelfResume();

        // act
        var externalUuid = Uuid(external, h);
        var selfUuid = Uuid(self, h);

        // assert
        WriteLine($"external={externalUuid}, self={selfUuid}");
        selfUuid.Should().NotBe(externalUuid, "an external resume must not absorb the flow's own");
        selfUuid.Should().StartWith($"r({flowId.Value})-self-at-",
            "FlowBackend.List and ListStats read the FlowId from what's in the parentheses");
        selfUuid.Should().EndWith($"-at-{self.DelayUntil.EpochOffsetTicks:x}", "the flow's own resumes still coalesce");
    }

    // Private methods

    private static string Uuid(FlowResumeEvent resumeEvent, TestAppHost h)
        => ((IOperationEventSource)resumeEvent).ToOperationEvent(h.Services).Uuid;
}
