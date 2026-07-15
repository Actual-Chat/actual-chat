using ActualChat.Bandwidth;
using ActualChat.Rpc;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class BandwidthEstimatorTest
{
    private const long InitialCeiling = 1_000_000;
    private static readonly Moment T0 = new(TimeSpan.FromSeconds(0));

    private static BandwidthEstimatorConfig Cfg()
        => new(InitialCeiling);

    private static BandwidthEstimator NewEstimator(BandwidthEstimatorConfig? config = null)
        => new(config ?? Cfg());

    private static RpcConnectionInfo Conn(int index = 1, double atSec = 0)
        => new(index, T0 + TimeSpan.FromSeconds(atSec));

    private static Moment At(double sec) => T0 + TimeSpan.FromSeconds(sec);

    // -------- Fresh-epoch defaults --------

    [Fact]
    public void FreshEstimator_HasInitialCeilingAndCleanState()
    {
        var e = NewEstimator();
        e.CeilingBps.Should().Be(InitialCeiling);
        e.EpochIndex.Should().Be(0);
        e.NegativeStreak.Should().Be(0);
        e.PositiveStreak.Should().Be(0);
        e.ProbeFailures.Should().Be(0);
        e.LastCeilingDownAt.Should().BeNull();
        e.History.Should().BeEmpty();
    }

    [Fact]
    public void ResetProbeBackoff_ClearsBackoffButKeepsCeilingAndStreaks()
    {
        // arrange
        var e = NewEstimator();
        var conn = Conn();
        // Two bad ticks: ratchets the ceiling down and anchors the back-off.
        e.Tick(conn, At(1), currentBandwidthBps: 200_000, signalLevel: 0.2);
        e.Tick(conn, At(2), currentBandwidthBps: 200_000, signalLevel: 0.2);
        e.LastCeilingDownAt.Should().NotBeNull();
        var ceilingBefore = e.CeilingBps;
        var negativeStreakBefore = e.NegativeStreak;

        // act
        e.ResetProbeBackoff();

        // assert
        e.LastCeilingDownAt.Should().BeNull();
        e.ProbeFailures.Should().Be(0);
        e.CeilingBps.Should().Be(ceilingBefore, "the back-off reset must not move the ceiling");
        e.NegativeStreak.Should().Be(negativeStreakBefore, "health streaks track health, not probe evidence");
    }

    // A demand increase clears the back-off, so the reprobe gate — which would
    // otherwise stay shut for up to MaxProbeCooldownSec — opens on the next
    // calm tick instead of holding a just-enlarged tile at a stale ceiling.
    [Fact]
    public void ResetProbeBackoff_ReopensTheReprobeGate()
    {
        // arrange
        var config = Cfg();
        var e = NewEstimator(config);
        var conn = Conn();
        e.Tick(conn, At(1), currentBandwidthBps: 200_000, signalLevel: 0.2);
        var downAt = e.LastCeilingDownAt;
        downAt.Should().NotBeNull();
        VideoQualityUI.ShouldReprobe(
                HealthVerdict.Good, HealthVerdict.Good, calmTicks: 3, reprobeCalmStreak: 3,
                downAt, e.ProbeFailures, At(2), config)
            .Should().BeFalse("the cooldown is still running");

        // act
        e.ResetProbeBackoff();

        // assert
        VideoQualityUI.ShouldReprobe(
                HealthVerdict.Good, HealthVerdict.Good, calmTicks: 3, reprobeCalmStreak: 3,
                e.LastCeilingDownAt, e.ProbeFailures, At(2), config)
            .Should().BeTrue();
    }

    [Fact]
    public void NullConnection_FreezesState()
    {
        var e = NewEstimator();
        var before = e.CeilingBps;

        e.Tick(connection: null, At(1), currentBandwidthBps: 500_000, signalLevel: 0.5);

        e.CeilingBps.Should().Be(before);
        e.EpochIndex.Should().Be(0);
        e.History.Should().BeEmpty();
    }

    // -------- Epoch transition --------

    [Fact]
    public void EpochTransition_ResetsAllState()
    {
        var e = NewEstimator();
        var conn1 = Conn(index: 1);

        for (var i = 0; i < 5; i++)
            e.Tick(conn1, At(i), 800_000, 0.5);

        e.EpochIndex.Should().Be(1);
        e.NegativeStreak.Should().BeGreaterThan(0);
        e.History.Should().NotBeEmpty();

        var conn2 = new RpcConnectionInfo(2, At(10));
        e.Tick(conn2, At(10), 900_000, 1.0);

        e.EpochIndex.Should().Be(2);
        e.NegativeStreak.Should().Be(0);
        e.PositiveStreak.Should().BeGreaterThanOrEqualTo(0);
        e.ProbeFailures.Should().Be(0);
        e.LastCeilingDownAt.Should().BeNull();
        e.History.Should().HaveCount(1);
    }

    // -------- Bad verdict lowers ceiling toward currentBps * signalLevel --------

    [Fact]
    public void BadVerdict_LowersCeilingTowardCurrentTimesSignalLevel()
    {
        var e = NewEstimator(Cfg() with { GoodThreshold = 0.95, BadThreshold = 0.85 });
        var conn = Conn();

        var ceilingBefore = e.CeilingBps;
        e.Tick(conn, At(1), currentBandwidthBps: 900_000, signalLevel: 0.5);

        e.LastVerdict.Should().Be(BandwidthVerdict.Bad);
        e.CeilingBps.Should().BeLessThan(ceilingBefore);
        e.LastCeilingDownAt.Should().NotBeNull();
        e.NegativeStreak.Should().Be(1);
        e.PositiveStreak.Should().Be(0);
    }

    [Fact]
    public void BadVerdict_StreakIncreasesAdjustmentMagnitude()
    {
        var e1 = NewEstimator();
        var e2 = NewEstimator();
        var conn = Conn();

        // First estimator: single bad tick at signal 0.5.
        e1.Tick(conn, At(1), 900_000, 0.5);

        // Second estimator: build up a negative streak before the same tick.
        for (var i = 0; i < 5; i++)
            e2.Tick(conn, At(i + 1), 900_000, 0.5);

        var drop1 = InitialCeiling - e1.CeilingBps;
        var drop2 = e2.History.First().CeilingBefore - e2.History.Last().CeilingAfter;

        drop2.Should().BeGreaterThan(drop1,
            "streaked negative ticks should compound — more total drop than a single tick");
    }

    [Fact]
    public void BadVerdict_LowerSignalLevelDropsCeilingHarder()
    {
        var eMild = NewEstimator();
        var eHarsh = NewEstimator();
        var conn = Conn();

        eMild.Tick(conn, At(1), 900_000, 0.84); // just under BadThreshold
        eHarsh.Tick(conn, At(1), 900_000, 0.10); // catastrophic

        var dropMild = InitialCeiling - eMild.CeilingBps;
        var dropHarsh = InitialCeiling - eHarsh.CeilingBps;
        dropHarsh.Should().BeGreaterThan(dropMild);
    }

    // -------- Good verdict lifts ceiling only when at-or-above ConfirmRatio --------

    [Fact]
    public void GoodVerdict_AtConfirmRatio_LiftsCeiling()
    {
        var e = NewEstimator();
        var conn = Conn();

        var ceilingBefore = e.CeilingBps;
        e.Tick(conn, At(1), currentBandwidthBps: 950_000, signalLevel: 1.0);

        e.LastVerdict.Should().Be(BandwidthVerdict.Good);
        e.CeilingBps.Should().BeGreaterThan(ceilingBefore);
        e.PositiveStreak.Should().Be(1);
    }

    [Fact]
    public void GoodVerdict_FarBelowCeiling_Holds()
    {
        var e = NewEstimator();
        var conn = Conn();

        var ceilingBefore = e.CeilingBps;
        e.Tick(conn, At(1), currentBandwidthBps: 100_000, signalLevel: 1.0);

        e.CeilingBps.Should().Be(ceilingBefore, "no new evidence — no lift");
    }

    [Fact]
    public void NeutralVerdict_HoldsCeiling()
    {
        var e = NewEstimator();
        var conn = Conn();

        var ceilingBefore = e.CeilingBps;
        e.Tick(conn, At(1), currentBandwidthBps: 950_000, signalLevel: 0.90);

        e.LastVerdict.Should().Be(BandwidthVerdict.Neutral);
        e.CeilingBps.Should().Be(ceilingBefore);
    }

    // -------- Unproven ceiling tracks observed usage upward --------

    [Fact]
    public void UnprovenCeiling_GrowsToTrackObservedUsage()
    {
        var e = NewEstimator();
        var conn = Conn();

        // No bad signal seen yet; usage above initial ceiling should lift it.
        e.Tick(conn, At(1), currentBandwidthBps: InitialCeiling * 3, signalLevel: 0.90);

        e.LastVerdict.Should().Be(BandwidthVerdict.Neutral);
        e.HasSeenBadSignal.Should().BeFalse();
        e.CeilingBps.Should().BeGreaterThan(InitialCeiling);
    }

    [Fact]
    public void ProvenCeiling_DoesNotGrowOnLowUsage()
    {
        var e = NewEstimator();
        var conn = Conn();

        // First a bad tick to prove the ceiling.
        e.Tick(conn, At(1), currentBandwidthBps: 900_000, signalLevel: 0.3);
        e.HasSeenBadSignal.Should().BeTrue();
        var ceilingAfterBad = e.CeilingBps;

        // Now a neutral tick at low usage should NOT grow the ceiling.
        e.Tick(conn, At(2), currentBandwidthBps: 100_000, signalLevel: 0.90);
        e.CeilingBps.Should().Be(ceilingAfterBad);
    }

    [Fact]
    public void HasSeenBadSignal_ResetsOnEpochTransition()
    {
        var e = NewEstimator();
        var conn1 = Conn(index: 1);
        e.Tick(conn1, At(1), 900_000, 0.3);
        e.HasSeenBadSignal.Should().BeTrue();

        var conn2 = new RpcConnectionInfo(2, At(10));
        e.Tick(conn2, At(10), 100_000, 1.0);
        e.HasSeenBadSignal.Should().BeFalse();
    }

    // -------- Floor --------

    [Fact]
    public void Ceiling_NeverFallsBelowFloor()
    {
        var e = NewEstimator(Cfg() with { FloorBps = 100_000, MaxStep = 1.0 });
        var conn = Conn();

        for (var i = 0; i < 30; i++)
            e.Tick(conn, At(i + 1), currentBandwidthBps: 80_000, signalLevel: 0.0);

        e.CeilingBps.Should().BeGreaterThanOrEqualTo(100_000);
    }

    // -------- Probe back-off (the cycle-widening test) --------

    [Fact]
    public void ProbeBackOff_CycleWidensWithFailures()
    {
        var cfg = Cfg() with {
            BaseProbeCooldownSec = 5,
            ProbeCooldownGrowth = 2.0,
            MaxProbeCooldownSec = 1000,
            ProbeFailureResetStreak = 1000,
        };
        var e = NewEstimator(cfg);
        var conn = Conn();
        var t = 0.0;

        // Cycle 1: probe up (good), then bad → ProbeFailures becomes 1.
        // After cycle 1, cooldown = Base * Growth^1 = 5 * 2 = 10s
        t += 1; e.Tick(conn, At(t), 950_000, 1.0); // probe up, PositiveStreak=1
        t += 1; e.Tick(conn, At(t), 950_000, 0.3); // bad after probe → failure
        e.ProbeFailures.Should().Be(1);
        var downAtCycle1 = e.LastCeilingDownAt!.Value;

        // 1s after the drop: still in cooldown (10s)
        t += 1; var ceilingBeforeAttempt = e.CeilingBps;
        e.Tick(conn, At(t), (long)(e.CeilingBps * 0.9), 1.0);
        e.CeilingBps.Should().Be(ceilingBeforeAttempt, "in cooldown — no lift");

        // Past cycle-1 cooldown (10s)
        t = downAtCycle1.EpochOffset.TotalSeconds + 11;
        ceilingBeforeAttempt = e.CeilingBps;
        e.Tick(conn, At(t), (long)(e.CeilingBps * 0.9), 1.0);
        e.CeilingBps.Should().BeGreaterThan(ceilingBeforeAttempt, "past cooldown — lift allowed");

        // Cycle 2: bad again → ProbeFailures = 2 → next cooldown = 5 * 2^2 = 20s
        t += 1; e.Tick(conn, At(t), 950_000, 0.3);
        e.ProbeFailures.Should().Be(2);
        var downAtCycle2 = e.LastCeilingDownAt!.Value;

        // 15s after cycle-2 drop: STILL in cooldown (cooldown is now 20s)
        t = downAtCycle2.EpochOffset.TotalSeconds + 15;
        ceilingBeforeAttempt = e.CeilingBps;
        e.Tick(conn, At(t), (long)(e.CeilingBps * 0.9), 1.0);
        e.CeilingBps.Should().Be(ceilingBeforeAttempt, "cycle 2 cooldown (20s) not yet elapsed");

        // 21s after cycle-2 drop: past cooldown
        t = downAtCycle2.EpochOffset.TotalSeconds + 21;
        ceilingBeforeAttempt = e.CeilingBps;
        e.Tick(conn, At(t), (long)(e.CeilingBps * 0.9), 1.0);
        e.CeilingBps.Should().BeGreaterThan(ceilingBeforeAttempt);
    }

    [Fact]
    public void ProbeBackOff_CooldownCappedAtMax()
    {
        var cfg = Cfg() with {
            BaseProbeCooldownSec = 1,
            ProbeCooldownGrowth = 10,
            MaxProbeCooldownSec = 5,
            ProbeFailureResetStreak = 1000,
        };
        var e = NewEstimator(cfg);
        var conn = Conn();
        var t = 0.0;

        // Force a big ProbeFailures count: probe up + bad, probe up + bad, ...
        for (var cycle = 0; cycle < 4; cycle++) {
            t += 1; e.Tick(conn, At(t), 950_000, 1.0);   // probe up
            t += 1; e.Tick(conn, At(t), 950_000, 0.3);   // bad → failure
            t += cfg.MaxProbeCooldownSec + 1;            // wait past cap
            e.Tick(conn, At(t), (long)(e.CeilingBps * 0.9), 1.0); // lift again
        }

        e.ProbeFailures.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void ProbeBackOff_ResetsAfterSustainedCalm()
    {
        var cfg = Cfg() with { ProbeFailureResetStreak = 5 };
        var e = NewEstimator(cfg);
        var conn = Conn();
        var t = 0.0;

        // Drive ProbeFailures up via one probe+bad cycle
        t += 1; e.Tick(conn, At(t), 950_000, 1.0); // probe up
        t += 1; e.Tick(conn, At(t), 950_000, 0.3); // bad → ProbeFailures = 1
        e.ProbeFailures.Should().Be(1);
        e.CalmTicks.Should().Be(0);

        // Run ResetStreak consecutive non-Bad ticks (idle/neutral) — calm accumulates
        for (var i = 0; i < cfg.ProbeFailureResetStreak; i++) {
            t += 1;
            e.Tick(conn, At(t), 100_000, 1.0); // idle good — well below ceiling
        }

        e.ProbeFailures.Should().Be(0, "calm streak should reset backoff");
        e.LastCeilingDownAt.Should().BeNull("calm reset clears cooldown anchor");
    }

    [Fact]
    public void StuckLowCap_ReanchorsCeilingAfterCalm_EnablingRecovery()
    {
        // Regression: once the cap is ratcheted to the bottom layer, the encoder
        // can only emit a low rate, which never reaches CeilingBps*ConfirmRatio,
        // so the ceiling can't be re-confirmed and the cap can never climb back.
        // After sustained calm the ceiling must re-anchor to the measured rate so
        // headroom becomes confirmable and recovery can proceed.
        var cfg = Cfg() with { ProbeFailureResetStreak = 5 };
        var e = NewEstimator(cfg);
        var conn = Conn();
        var t = 0.0;

        // Prove the ceiling with a bad tick — it stays well above the low rate we
        // can produce while pinned to the bottom layer.
        t += 1; e.Tick(conn, At(t), currentBandwidthBps: 900_000, signalLevel: 0.3);
        e.HasSeenBadSignal.Should().BeTrue();
        var stuckCeiling = e.CeilingBps;

        const long lowRate = 150_000; // bottom-layer-only output
        (lowRate >= stuckCeiling * cfg.ConfirmRatio)
            .Should().BeFalse("precondition: low rate can't confirm headroom against the stuck ceiling");

        // Sustained calm at the low (capped) rate — Good signal but no headroom.
        for (var i = 0; i < cfg.ProbeFailureResetStreak; i++) {
            t += 1; e.Tick(conn, At(t), lowRate, signalLevel: 1.0);
        }

        e.CeilingBps.Should().BeLessThan(stuckCeiling, "ceiling re-anchored toward the measured rate");
        (lowRate >= e.CeilingBps * cfg.ConfirmRatio)
            .Should().BeTrue("re-anchor makes headroom confirmable again");

        // Next good tick now confirms headroom and lifts the ceiling — the cap can climb.
        var beforeLift = e.CeilingBps;
        t += 1; e.Tick(conn, At(t), lowRate, signalLevel: 1.0);
        e.CeilingBps.Should().BeGreaterThanOrEqualTo(beforeLift);
        e.PositiveStreak.Should().BeGreaterThan(0,
            "good-with-headroom ticks accumulate a positive streak so BandwidthCap.Increase can fire");
    }

    [Fact]
    public void ApplyMeasuredCapacity_AnchorsCeilingAndEnablesHeadroom()
    {
        var e = NewEstimator();
        var conn = Conn();
        // A bad tick proves the ceiling but leaves it well above the true rate.
        e.Tick(conn, At(1), 900_000, 0.3);
        e.HasSeenBadSignal.Should().BeTrue();

        // Backlogged drain rate of 300_000 B/s lagging a 900_000 B/s produce
        // rate = the real capacity.
        e.ApplyMeasuredCapacity(300_000, 900_000, At(2));

        e.CeilingBps.Should().Be(300_000, "the ceiling anchors to the measurement, even after a bad signal");
        e.LastCurrentBps.Should().Be(300_000);
        e.LastCeilingDownAt.Should().BeNull("a measurement isn't a failed probe");
        (e.LastCurrentBps >= e.CeilingBps * e.Config.ConfirmRatio)
            .Should().BeTrue("headroom is now confirmable at the measured rate");
    }

    [Fact]
    public void ApplyMeasuredCapacity_FloorsAndIgnoresNonPositive()
    {
        var e = NewEstimator(Cfg() with { FloorBps = 100_000 });
        var conn = Conn();
        e.Tick(conn, At(1), 50_000, 1.0);
        var before = e.CeilingBps;

        e.ApplyMeasuredCapacity(0, 50_000, At(2));
        e.CeilingBps.Should().Be(before, "non-positive measurement is ignored");

        e.ApplyMeasuredCapacity(10_000, 50_000, At(3));
        e.CeilingBps.Should().Be(100_000, "measurement below the floor is clamped to the floor");
    }

    [Fact]
    public void ApplyMeasuredCapacity_SkipsDownwardAnchorOnCreditStall()
    {
        var e = NewEstimator();
        var conn = Conn();
        e.Tick(conn, At(1), 310_000, 1.0);
        var ceilingBefore = e.CeilingBps;
        var currentBefore = e.LastCurrentBps;

        // Backlog draining at ~the produce rate is a credit-window (RTT) stall,
        // not a link bottleneck — the low drain rate must not become the ceiling.
        e.ApplyMeasuredCapacity(300_000, 310_000, At(2));

        e.CeilingBps.Should().Be(ceilingBefore, "drain keeping pace with production isn't a capacity measurement");
        e.LastCurrentBps.Should().Be(currentBefore);
    }

    [Fact]
    public void ApplyMeasuredCapacity_AnchorsUpwardRegardlessOfProduction()
    {
        var e = NewEstimator();
        var conn = Conn();
        e.Tick(conn, At(1), 900_000, 0.3);

        e.ApplyMeasuredCapacity(1_500_000, 1_500_000, At(2));

        e.CeilingBps.Should().Be(1_500_000, "a drain rate above the ceiling proves capacity regardless of production");
    }

    [Fact]
    public void CalmTicks_ResetOnBadVerdict()
    {
        var e = NewEstimator(Cfg() with { ProbeFailureResetStreak = 100 });
        var conn = Conn();
        var t = 0.0;

        for (var i = 0; i < 5; i++) {
            t += 1;
            e.Tick(conn, At(t), 100_000, 1.0);
        }
        e.CalmTicks.Should().Be(5);

        t += 1;
        e.Tick(conn, At(t), 950_000, 0.3);
        e.CalmTicks.Should().Be(0);
    }

    // -------- History ring buffer --------

    [Fact]
    public void History_TrimsToConfiguredSize()
    {
        var cfg = Cfg() with { HistorySize = 3 };
        var e = NewEstimator(cfg);
        var conn = Conn();

        for (var i = 0; i < 10; i++)
            e.Tick(conn, At(i + 1), 900_000, 1.0);

        e.History.Should().HaveCount(3);
    }

    [Fact]
    public void History_RecordsAllFields()
    {
        var e = NewEstimator();
        var conn = Conn();
        var ceilingBefore = e.CeilingBps;

        e.Tick(conn, At(1), currentBandwidthBps: 950_000, signalLevel: 1.0);

        e.History.Should().HaveCount(1);
        var r = e.History.Single();
        r.At.Should().Be(At(1));
        r.TriedBps.Should().Be(950_000);
        r.SignalLevel.Should().Be(1.0);
        r.CeilingBefore.Should().Be(ceilingBefore);
        r.CeilingAfter.Should().Be(e.CeilingBps);
        r.Verdict.Should().Be(BandwidthVerdict.Good);
    }

    // -------- Pluggable classifier --------

    [Fact]
    public void CustomClassifier_IsHonored()
    {
        var cfg = Cfg() with {
            Classify = _ => BandwidthVerdict.Bad,
        };
        var e = NewEstimator(cfg);
        var conn = Conn();

        // signalLevel = 0.96 would classify as Good under default thresholds;
        // the override forces Bad. The (1-signalLevel)=0.04 severity term is
        // tiny but nonzero, so we expect a small drop.
        e.Tick(conn, At(1), 950_000, 0.96);

        e.LastVerdict.Should().Be(BandwidthVerdict.Bad);
        e.CeilingBps.Should().BeLessThan(InitialCeiling);
    }

    // -------- Age-decay scaling --------

    [Fact]
    public void AgeFactor_DampensAdjustmentsOnOlderConnections()
    {
        var young = NewEstimator(Cfg() with { TauSec = 10, MinAgeFactor = 0.1 });
        var old = NewEstimator(Cfg() with { TauSec = 10, MinAgeFactor = 0.1 });

        var youngConn = new RpcConnectionInfo(1, At(0));    // just connected
        var oldConn = new RpcConnectionInfo(1, At(-1000));  // connected long ago

        young.Tick(youngConn, At(0.001), 900_000, 0.3);
        old.Tick(oldConn, At(0.001), 900_000, 0.3);

        var youngDrop = InitialCeiling - young.CeilingBps;
        var oldDrop = InitialCeiling - old.CeilingBps;

        youngDrop.Should().BeGreaterThan(oldDrop, "young connection moves faster than old");
    }
}
