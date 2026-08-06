using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;

namespace ActualChat.Core.Server.UnitTests.Flows;

// FlowResumeEvent has [SerializationConstructor] on a private parameterless-ish constructor
// (taking only FlowId), and several `private set` properties (MustReset, DelayUntil, DelayQuanta).
// MessagePack-CSharp's source generator handles private setters when [SerializationConstructor]
// is present, so no public-setter / AllowPrivate workaround is needed here.
public class FlowResumeEventSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void MessagePack_RoundTrip_PreservesAllFields_WithReset()
    {
        var flowId = new FlowId("TestFlow", "args");
        var when = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc));
        var original = CreateTestEvent(flowId)
            .WithDelay(when)
            .WithDelayQuanta(TimeSpan.FromSeconds(5))
            .WithReset();

        var roundTripped = original.PassThroughMessagePackByteSerializer(Out);

        roundTripped.FlowId.Should().Be(original.FlowId);
        roundTripped.MustReset.Should().BeTrue();
        roundTripped.DelayUntil.Should().Be(when);
        // DelayQuanta getter returns Zero when MustReset is true; verify via a separate test
        // that the underlying field is preserved.
        roundTripped.DelayQuanta.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void MessagePack_RoundTrip_PreservesDelayQuanta_NoReset()
    {
        var flowId = new FlowId("TestFlow", "args");
        var when = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc));
        var original = CreateTestEvent(flowId)
            .WithDelay(when)
            .WithDelayQuanta(TimeSpan.FromSeconds(5));

        var roundTripped = original.PassThroughMessagePackByteSerializer(Out);

        roundTripped.FlowId.Should().Be(original.FlowId);
        roundTripped.MustReset.Should().BeFalse();
        roundTripped.DelayUntil.Should().Be(when);
        roundTripped.DelayQuanta.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MessagePack_RoundTrip_Default()
    {
        var flowId = new FlowId("TestFlow", "args");
        var original = CreateTestEvent(flowId);

        var roundTripped = original.PassThroughMessagePackByteSerializer(Out);

        roundTripped.FlowId.Should().Be(original.FlowId);
        roundTripped.MustReset.Should().BeFalse();
        roundTripped.DelayUntil.Should().Be(default(Moment));
        roundTripped.DelayQuanta.Should().BeNull();
    }

    // Build a FlowResumeEvent without a FlowHub by invoking the private SerializationConstructor.
    private static FlowResumeEvent CreateTestEvent(FlowId flowId)
    {
        var ctor = typeof(FlowResumeEvent).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(FlowId)]);
        return (FlowResumeEvent)ctor!.Invoke([flowId]);
    }
}
