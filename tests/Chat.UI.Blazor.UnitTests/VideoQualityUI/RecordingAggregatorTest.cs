using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using static ActualChat.UI.Blazor.App.Services.VideoQualityUI;

namespace ActualChat.Chat.UI.Blazor.UnitTests.VideoQualityUITests;

public class RecordingAggregatorTest
{
    private static readonly RecordingThresholds T = RecordingThresholds.Defaults;

    [Fact]
    public void StartsAtMaxLayerCount()
    {
        var agg = new RecordingAggregator(T);
        agg.TargetLayerCount.Should().Be(T.MaxTargetLayerCount);
    }

    [Fact]
    public void ConsecutiveGoodSignals_DoNotClimbBeforeK()
    {
        // arrange
        var agg = new RecordingAggregator(T);
        // First step down so there's room to climb
        var d = agg.Step(-1);
        d.Changed.Should().BeTrue();
        d.Reason.Should().Be(RecordingQualityReason.Backoff);
        var afterBackoff = agg.TargetLayerCount;
        // Wait out cooldown
        for (var i = 0; i < T.CooldownTicksAfterBackoff; i++) {
            agg.Step(0); // hold during cooldown
        }

        // act — feed K-1 good signals
        for (var i = 0; i < T.ConsecutiveGoodForClimb - 1; i++) {
            var step = agg.Step(1);
            step.Changed.Should().BeFalse($"step {i} should not climb yet");
        }

        // assert — still at the post-backoff level
        agg.TargetLayerCount.Should().Be(afterBackoff);
    }

    [Fact]
    public void K_ConsecutiveGoodSignals_ClimbOneStep()
    {
        // arrange
        var agg = new RecordingAggregator(T);
        agg.Step(-1); // backoff to MaxTarget - 1
        for (var i = 0; i < T.CooldownTicksAfterBackoff; i++) agg.Step(0);
        var beforeClimb = agg.TargetLayerCount;

        // act
        RecordingDecision? climbDecision = null;
        for (var i = 0; i < T.ConsecutiveGoodForClimb; i++) {
            climbDecision = agg.Step(1);
        }

        // assert
        climbDecision.Should().NotBeNull();
        climbDecision!.Changed.Should().BeTrue();
        climbDecision.Reason.Should().Be(RecordingQualityReason.Climb);
        agg.TargetLayerCount.Should().Be(beforeClimb + 1);
    }

    [Fact]
    public void BadSignal_StepsDownInstantly()
    {
        // arrange
        var agg = new RecordingAggregator(T);

        // act
        var d = agg.Step(-1);

        // assert
        d.Changed.Should().BeTrue();
        d.Reason.Should().Be(RecordingQualityReason.Backoff);
        agg.TargetLayerCount.Should().Be(T.MaxTargetLayerCount - 1);
    }

    [Fact]
    public void Cooldown_BlocksClimb()
    {
        // arrange
        var agg = new RecordingAggregator(T);
        agg.Step(-1); // backoff sets cooldown
        var afterBackoff = agg.TargetLayerCount;

        // act — bombarded with good signals during cooldown, no climb
        var maxObserved = afterBackoff;
        for (var i = 0; i < T.CooldownTicksAfterBackoff; i++) {
            var d = agg.Step(1);
            d.Changed.Should().BeFalse("cooldown blocks climb");
            maxObserved = Math.Max(maxObserved, agg.TargetLayerCount);
        }

        // assert
        maxObserved.Should().Be(afterBackoff);
    }

    [Fact]
    public void FloorAtMin_BadSignalReportsStuckAtFloor()
    {
        // arrange
        var agg = new RecordingAggregator(T);
        // Drive down to the floor
        while (agg.TargetLayerCount > T.MinTargetLayerCount) {
            agg.Step(-1);
            for (var i = 0; i < T.CooldownTicksAfterBackoff; i++) agg.Step(0);
        }

        // act
        var d = agg.Step(-1);

        // assert
        d.Changed.Should().BeFalse();
        d.Reason.Should().Be(RecordingQualityReason.StuckAtFloor);
        agg.TargetLayerCount.Should().Be(T.MinTargetLayerCount);
    }

    [Fact]
    public void NeutralSignal_ResetsConsecutiveGood()
    {
        // arrange
        var agg = new RecordingAggregator(T);
        agg.Step(-1); // backoff
        for (var i = 0; i < T.CooldownTicksAfterBackoff; i++) agg.Step(0);

        // act — accumulate K-1 good, then a neutral, then K-1 good again
        for (var i = 0; i < T.ConsecutiveGoodForClimb - 1; i++)
            agg.Step(1).Changed.Should().BeFalse();
        agg.Step(0); // resets consecutiveGood

        var before = agg.TargetLayerCount;
        for (var i = 0; i < T.ConsecutiveGoodForClimb - 1; i++)
            agg.Step(1).Changed.Should().BeFalse();

        // assert
        agg.TargetLayerCount.Should().Be(before);
    }

    [Fact]
    public void Reset_RestoresMaxTargetLayer()
    {
        // arrange
        var agg = new RecordingAggregator(T);
        agg.Step(-1); // backoff once

        // act
        agg.Reset();

        // assert
        agg.TargetLayerCount.Should().Be(T.MaxTargetLayerCount);
    }

    [Fact]
    public void Snapshot_ReportsBothFields()
    {
        // arrange
        var agg = new RecordingAggregator(T);
        agg.Step(-1);
        var expectedTarget = agg.TargetLayerCount;

        // act
        var snap = agg.Snapshot();

        // assert
        snap.TargetLayerCount.Should().Be(expectedTarget);
        snap.EffectiveLayerCount.Should().Be(expectedTarget);
    }
}
