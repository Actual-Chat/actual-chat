using ActualChat.UI.Blazor.App.Services.Gestures;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class GestureDetectorTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    private static SensorSample Portrait(double atMs) => new(At(atMs), 0f, -1f, 0f);
    private static SensorSample Landscape(double atMs) => new(At(atMs), -1f, 0f, 0f);
    private static SensorSample FaceUp(double atMs) => new(At(atMs), 0f, 0f, -1f);
    private static SensorSample FaceDown(double atMs) => new(At(atMs), 0f, 0f, 1f);
    private static Moment At(double ms) => T0 + TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Flip_FiresOnPortraitLandscapePortrait()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Landscape(600)).Should().BeFalse();
        d.Process(Portrait(900)).Should().BeTrue();
    }

    [Fact]
    public void Flip_FiresOnRealisticRotation()
    {
        // A deliberate flip at SensorSpeed.UI's ~60ms cadence: the samples taken while the phone
        // is actually turning are dynamic and get skipped, so only the settled ones classify.
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Portrait(60)).Should().BeFalse();
        d.Process(new SensorSample(At(120), -1.6f, -1.0f, 0.5f)).Should().BeFalse();
        foreach (var atMs in new[] { 180, 240, 300, 360, 420 })
            d.Process(Landscape(atMs)).Should().BeFalse($"landscape at {atMs}ms is not the end of a flip");
        d.Process(new SensorSample(At(480), -1.3f, -1.4f, 0.3f)).Should().BeFalse();
        d.Process(Portrait(540)).Should().BeTrue();
    }

    [Fact]
    public void Flip_DoesNotFireOnHalfRotation()
    {
        // Portrait -> landscape held well past the dwell is only half a flip: without a return
        // to portrait no sample may fire, however many landscape readings arrive.
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        for (var atMs = 60; atMs <= 5000; atMs += 60)
            d.Process(Landscape(atMs)).Should().BeFalse($"landscape at {atMs}ms is not a flip");
    }

    [Fact]
    public void Flip_DoesNotFireOnBriefLandscapeBlip()
    {
        // One landscape sample is a blip, not a rotation - the dwell is what tells them apart.
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(60)).Should().BeFalse();
        d.Process(Portrait(120)).Should().BeFalse();
    }

    [Fact]
    public void Flip_DoesNotFireOnLateralJolt()
    {
        // An arm swing pushes |X| past the dominance threshold while |a| is nowhere near 1g:
        // that's hand acceleration, not gravity, so it must not read as landscape.
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(new SensorSample(At(60), -1.4f, -1.1f, 0.3f)).Should().BeFalse();
        d.Process(new SensorSample(At(120), -2.2f, -0.6f, 0.2f)).Should().BeFalse();
        d.Process(new SensorSample(At(180), -1.5f, -0.9f, 0.1f)).Should().BeFalse();
        d.Process(new SensorSample(At(240), -1.2f, -1.3f, 0.2f)).Should().BeFalse();
        d.Process(Portrait(300)).Should().BeFalse();
        d.Process(Portrait(360)).Should().BeFalse();
    }

    [Fact]
    public void Flip_DoesNotFireWhenReturnExceedsWindow()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Landscape(600)).Should().BeFalse();
        d.Process(Portrait(4000)).Should().BeFalse();
    }

    [Fact]
    public void Flip_DoesNotFireThroughFlat()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(200)).Should().BeFalse();
        d.Process(Landscape(500)).Should().BeFalse();
        d.Process(FaceUp(700)).Should().BeFalse();
        d.Process(Portrait(900)).Should().BeFalse();
    }

    [Fact]
    public void Shake_FiresOnAlternatingSpikes()
    {
        var d = new ShakeDetector(ShakeSensitivity.Medium);
        Shake(d, ShakeSensitivity.Medium, reversals: 3, stepMs: 80).Should().BeTrue();
    }

    [Fact]
    public void Shake_DoesNotFireOnSingleSpike()
    {
        var d = new ShakeDetector(ShakeSensitivity.Medium);
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(new SensorSample(At(50), 0f, -3f, 0f)).Should().BeFalse();
        d.Process(Portrait(100)).Should().BeFalse();
    }

    [Fact]
    public void Shake_DoesNotFireWhenSpikesAreTooSlow()
    {
        var d = new ShakeDetector(ShakeSensitivity.Medium);
        Shake(d, ShakeSensitivity.Medium, reversals: 3, stepMs: 400).Should().BeFalse();
    }

    [Fact]
    public void Shake_HonoursDebounce()
    {
        var d = new ShakeDetector(ShakeSensitivity.High);
        Shake(d, ShakeSensitivity.High, reversals: 3, stepMs: 60).Should().BeTrue();
        Shake(d, ShakeSensitivity.High, reversals: 3, stepMs: 60, startMs: 400).Should().BeFalse();
        Shake(d, ShakeSensitivity.High, reversals: 3, stepMs: 60, startMs: 3000).Should().BeTrue();
    }

    [Theory]
    [InlineData(ShakeSensitivity.Low)]
    [InlineData(ShakeSensitivity.Medium)]
    public void Shake_SensitivityIsMonotonic(ShakeSensitivity fired)
    {
        // Anything that fires at a lower sensitivity must also fire at a higher one.
        var reversals = ShakeDetector.GetReversalCount(fired);
        var stronger = fired == ShakeSensitivity.Low ? ShakeSensitivity.Medium : ShakeSensitivity.High;
        Shake(new ShakeDetector(fired), fired, reversals, stepMs: 70).Should().BeTrue();
        Shake(new ShakeDetector(stronger), fired, reversals, stepMs: 70).Should().BeTrue();
    }

    [Fact]
    public void FaceDown_FiresAfterDwell()
    {
        var d = new FaceDownDetector();
        d.Process(FaceDown(0)).Should().BeFalse();
        d.Process(FaceDown(400)).Should().BeFalse();
        d.Process(FaceDown(1200)).Should().BeTrue();
    }

    [Fact]
    public void FaceDown_DoesNotFireOnTransientPickUp()
    {
        var d = new FaceDownDetector();
        d.Process(FaceDown(0)).Should().BeFalse();
        d.Process(FaceDown(200)).Should().BeFalse();
        d.Process(Portrait(400)).Should().BeFalse();
        d.Process(FaceDown(600)).Should().BeFalse();
    }

    [Fact]
    public void FaceDown_FiresOnCoveredAndUpright()
    {
        var d = new FaceDownDetector();
        d.SetProximityCovered(true);
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Portrait(1200)).Should().BeTrue();
    }

    [Fact]
    public void FaceDown_UprightAloneDoesNotFire()
    {
        var d = new FaceDownDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Portrait(5000)).Should().BeFalse();
    }

    [Fact]
    public void Recognizer_RoutesOnlyToEnabledDetectors()
    {
        var options = new GestureOptions(false, true, true, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        r.Process(Portrait(0));
        r.Process(Landscape(300));
        r.Process(Portrait(600)).Should().BeNull();
    }

    [Fact]
    public void Recognizer_StopBeatsStart()
    {
        var options = new GestureOptions(true, true, true, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        r.SetProximityCovered(true);
        // Face-down (700ms dwell since t=0) and shake (2 High-sensitivity |a| reversals within
        // the last 500ms: dip at 650, rise at 700) both complete on the very last sample - it
        // must report the stop, never the start, because GestureRecognizer.Process evaluates
        // face-down first.
        r.Process(new SensorSample(At(0), 0f, 0f, 3f)).Should().BeNull();
        r.Process(new SensorSample(At(600), 0f, 0f, 1f)).Should().BeNull();
        r.Process(new SensorSample(At(650), 0f, 0f, 0.2f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(700), 0f, 0f, 3f));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void Shake_DipOnly0_5FiresOnlyAtHigh()
    {
        var samples = new[] {
            new SensorSample(At(0), 0f, -3f, 0f),
            new SensorSample(At(100), 0f, -0.5f, 0f),
            new SensorSample(At(200), 0f, -3f, 0f),
        };
        HasFired(samples, ShakeSensitivity.Low).Should().BeFalse();
        HasFired(samples, ShakeSensitivity.Medium).Should().BeFalse();
        HasFired(samples, ShakeSensitivity.High).Should().BeTrue();
    }

    [Fact]
    public void Shake_DipAt0_35FiresAtAllSensitivities()
    {
        var samples = new[] {
            new SensorSample(At(0), 0f, -3f, 0f),
            new SensorSample(At(80), 0f, -0.35f, 0f),
            new SensorSample(At(160), 0f, -3f, 0f),
            new SensorSample(At(240), 0f, -0.35f, 0f),
            new SensorSample(At(320), 0f, -3f, 0f),
        };
        HasFired(samples, ShakeSensitivity.Low).Should().BeTrue();
        HasFired(samples, ShakeSensitivity.Medium).Should().BeTrue();
        HasFired(samples, ShakeSensitivity.High).Should().BeTrue();
    }

    [Fact]
    public void Shake_RiseAt1_6FiresOnlyAtHigh()
    {
        var samples = new[] {
            new SensorSample(At(0), 0f, 0f, 0f),
            new SensorSample(At(80), 0f, -1.6f, 0f),
            new SensorSample(At(160), 0f, 0f, 0f),
        };
        HasFired(samples, ShakeSensitivity.Low).Should().BeFalse();
        HasFired(samples, ShakeSensitivity.Medium).Should().BeFalse();
        HasFired(samples, ShakeSensitivity.High).Should().BeTrue();
    }

    [Fact]
    public void Shake_RiseAt2_3FiresAtAllSensitivities()
    {
        var samples = new[] {
            new SensorSample(At(0), 0f, 0f, 0f),
            new SensorSample(At(80), 0f, -2.3f, 0f),
            new SensorSample(At(160), 0f, 0f, 0f),
            new SensorSample(At(240), 0f, -2.3f, 0f),
            new SensorSample(At(320), 0f, 0f, 0f),
        };
        HasFired(samples, ShakeSensitivity.Low).Should().BeTrue();
        HasFired(samples, ShakeSensitivity.Medium).Should().BeTrue();
        HasFired(samples, ShakeSensitivity.High).Should().BeTrue();
    }

    [Fact]
    public void Shake_SensitivityNestsAcrossRandomSequences()
    {
        var random = new Random(20260803);
        for (var trial = 0; trial < 5000; trial++) {
            var sampleCount = random.Next(4, 12);
            var samples = new SensorSample[sampleCount];
            var t = 0.0;
            for (var i = 0; i < sampleCount; i++) {
                t += 60 + (random.NextDouble() * (700 - 60));
                var magnitude = (float)(random.NextDouble() * 3.0);
                samples[i] = new SensorSample(At(t), 0f, -magnitude, 0f);
            }

            var hasFiredLow = HasFired(samples, ShakeSensitivity.Low);
            var hasFiredMedium = HasFired(samples, ShakeSensitivity.Medium);
            var hasFiredHigh = HasFired(samples, ShakeSensitivity.High);
            if (hasFiredLow)
                hasFiredMedium.Should().BeTrue($"trial {trial} fired at Low but not Medium");
            if (hasFiredMedium)
                hasFiredHigh.Should().BeTrue($"trial {trial} fired at Medium but not High");
        }
    }

    [Fact]
    public void Shake_ChangeSensitivityPreservesDebounceAndPeak()
    {
        var d = new ShakeDetector(ShakeSensitivity.High);
        Shake(d, ShakeSensitivity.High, ShakeDetector.GetReversalCount(ShakeSensitivity.High), stepMs: 60)
            .Should().BeTrue();
        var peakBeforeChange = d.PeakDeviation;
        peakBeforeChange.Should().BeGreaterThan(0f);
        d.ChangeSensitivity(ShakeSensitivity.Low);
        d.PeakDeviation.Should().Be(peakBeforeChange);
        // The debounce set by the High-sensitivity fire must still block a fresh Low-sensitivity
        // burst that starts well inside the debounce window.
        Shake(d, ShakeSensitivity.Low, ShakeDetector.GetReversalCount(ShakeSensitivity.Low), stepMs: 60, startMs: 400)
            .Should().BeFalse();
    }

    [Fact]
    public void Recognizer_EnableToggleResetsFaceDownDwell()
    {
        var options = new GestureOptions(false, false, true, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        r.Process(new SensorSample(At(0), 0f, 0f, 1f)).Should().BeNull();
        r.Process(new SensorSample(At(400), 0f, 0f, 1f)).Should().BeNull();
        r.Options = options with { IsFaceDownEnabled = false };
        r.Options = options with { IsFaceDownEnabled = true };
        // Without the reset-on-toggle fix, the stale _heldSince(0) would make this look like the
        // dwell already elapsed (1000ms since t=0), firing immediately.
        r.Process(new SensorSample(At(1000), 0f, 0f, 1f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(1750), 0f, 0f, 1f));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void Recognizer_SampleGapResetsFaceDownDwell()
    {
        var options = new GestureOptions(false, false, true, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        r.Process(new SensorSample(At(0), 0f, 0f, 1f)).Should().BeNull();
        r.Process(new SensorSample(At(400), 0f, 0f, 1f)).Should().BeNull();
        // A 2.5s gap simulates the app being backgrounded and resumed - without the gap guard,
        // the stale _heldSince(0) would make this look like the dwell already elapsed.
        r.Process(new SensorSample(At(2900), 0f, 0f, 1f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(3650), 0f, 0f, 1f));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    private static bool HasFired(IEnumerable<SensorSample> samples, ShakeSensitivity sensitivity)
    {
        var detector = new ShakeDetector(sensitivity);
        var hasFired = false;
        foreach (var sample in samples)
            hasFired |= detector.Process(sample);
        return hasFired;
    }

    private static bool Shake(
        ShakeDetector detector,
        ShakeSensitivity sensitivity,
        int reversals,
        double stepMs,
        double startMs = 0)
    {
        // Alternating |a| spikes above and below 1g by more than the sensitivity threshold.
        var threshold = ShakeDetector.GetMagnitudeThreshold(sensitivity) + 0.2f;
        var hasFired = false;
        for (var i = 0; i <= reversals; i++) {
            var magnitude = i % 2 == 0 ? 1f + threshold : Math.Max(0f, 1f - threshold);
            var sample = new SensorSample(At(startMs + (i * stepMs)), 0f, -magnitude, 0f);
            hasFired |= detector.Process(sample);
        }
        return hasFired;
    }
}
