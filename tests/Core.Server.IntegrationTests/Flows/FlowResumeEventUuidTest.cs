using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Testing.Host;
using ActualLab.CommandR.Operations;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// A quantized Uuid is how resumes landing in the same slot get coalesced - by FlushEvents' Skip on
// the DbEvents path, and by the queue's dedup MsgId. Applying it to an immediate resume gives every
// immediate resume of a flow one shared Uuid, so the second one is dropped rather than run.

[Trait("Category", "Slow")]
public sealed class FlowResumeEventUuidTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(FlowResumeEventUuidTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.AddFlows(useMasterFlows: false).Add<QuantaFlow>(),
    }, @out)
{
    private static readonly TimeSpan Quanta = TimeSpan.FromSeconds(30);

    [Fact(Timeout = 120_000)]
    public async Task ImmediateResumesGetDistinctUuids()
    {
        // arrange
        await using var h = await NewAppHost();
        var flowHub = h.Services.FlowHub();
        var flowId = flowHub.NewId<QuantaFlow>("uuids,1");

        // act
        var first = Uuid(flowHub.NewResumeEvent(flowId), h);
        var second = Uuid(flowHub.NewResumeEvent(flowId), h);

        // assert
        WriteLine($"first={first}, second={second}");
        first.Should().NotEndWith("-at-0", because: "an unset DelayUntil must not quantize to a constant");
        second.Should().NotBe(first, because: "two immediate resumes are two wake-ups, not one slot");
    }

    [Fact(Timeout = 120_000)]
    public async Task AZeroDelayResumeIsNotQuantizedAndKeepsItsDelayUntil()
    {
        // arrange
        await using var h = await NewAppHost();
        var flowHub = h.Services.FlowHub();
        var flowId = flowHub.NewId<QuantaFlow>("uuids,2");
        var resumeEvent = flowHub.NewResumeEvent(flowId).WithDelay(TimeSpan.Zero);
        var requestedAt = resumeEvent.DelayUntil;

        // act
        var uuid = Uuid(resumeEvent, h);

        // assert
        WriteLine($"uuid={uuid}, delayUntil={resumeEvent.DelayUntil:O}");
        uuid.Should().StartWith($"{nameof(FlowResumeEvent)}-",
            because: "an immediate resume has no slot to coalesce into");
        resumeEvent.DelayUntil.Should().Be(requestedAt,
            because: "rounding it up would make ShardQueueProcessor postpone an immediate resume");
    }

    [Fact(Timeout = 120_000)]
    public async Task ScheduledResumesInOneSlotShareAUuidAndItsDelayUntil()
    {
        // arrange
        await using var h = await NewAppHost();
        var flowHub = h.Services.FlowHub();
        var flowId = flowHub.NewId<QuantaFlow>("uuids,3");
        var slotStart = (flowHub.SystemNow + TimeSpan.FromMinutes(1)).Ceiling(Quanta);
        var earlier = flowHub.NewResumeEvent(flowId).WithDelay(slotStart - Quanta + TimeSpan.FromSeconds(1), Quanta);
        var later = flowHub.NewResumeEvent(flowId).WithDelay(slotStart - TimeSpan.FromSeconds(1), Quanta);

        // act
        var earlierUuid = Uuid(earlier, h);
        var laterUuid = Uuid(later, h);

        // assert
        WriteLine($"earlier={earlierUuid} @ {earlier.DelayUntil:O}");
        WriteLine($"later  ={laterUuid} @ {later.DelayUntil:O}");
        laterUuid.Should().Be(earlierUuid, because: "resumes in one slot must still coalesce");
        earlierUuid.Should().EndWith($"-at-{slotStart.EpochOffsetTicks:x}");
        earlier.DelayUntil.Should().Be(slotStart, because: "the slot is the schedule");
        later.DelayUntil.Should().Be(slotStart, because: "the slot is the schedule");
    }

    // Private methods

    private static string Uuid(FlowResumeEvent resumeEvent, TestAppHost h)
        => ((IOperationEventSource)resumeEvent).ToOperationEvent(h.Services).Uuid;
}
