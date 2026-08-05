using ActualChat.UI.Blazor.App.Services.Gestures;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class GestureDetectorTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    private static SensorSample Portrait(double atMs) => new(At(atMs), 0f, -1f, 0f);
    private static SensorSample Landscape(double atMs) => new(At(atMs), -1f, 0f, 0f);
    private static SensorSample FaceUp(double atMs) => new(At(atMs), 0f, 0f, 1f);
    private static SensorSample FaceDown(double atMs) => new(At(atMs), 0f, 0f, -1f);
    private static Moment At(double ms) => T0 + TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void FlipFiresOnPortraitLandscapePortrait()
    {
        var d = new FlipToTalkDetector();
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Landscape(600)).Should().BeFalse();
        d.Process(Portrait(900)).Should().BeTrue();
    }

    [Fact]
    public void FlipFiresOnRealisticRotation()
    {
        // A deliberate flip at SensorSpeed.UI's ~60ms cadence: the samples taken while the phone
        // is actually turning are dynamic and get skipped, so only the settled ones classify.
        var d = new FlipToTalkDetector();
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Portrait(60)).Should().BeFalse();
        d.Process(new SensorSample(At(120), -1.6f, -1.0f, 0.5f)).Should().BeFalse();
        foreach (var atMs in new[] { 180, 240, 300, 360, 420 })
            d.Process(Landscape(atMs)).Should().BeFalse($"landscape at {atMs}ms is not the end of a flip");
        d.Process(new SensorSample(At(480), -1.3f, -1.4f, 0.3f)).Should().BeFalse();
        d.Process(Portrait(540)).Should().BeTrue();
    }

    [Fact]
    public void FlipDoesNotFireOnHalfRotation()
    {
        // Portrait -> landscape held well past the dwell is only half a flip: without a return
        // to portrait no sample may fire, however many landscape readings arrive.
        var d = new FlipToTalkDetector();
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        for (var atMs = 60; atMs <= 5000; atMs += 60)
            d.Process(Landscape(atMs)).Should().BeFalse($"landscape at {atMs}ms is not a flip");
    }

    [Fact]
    public void FlipDoesNotFireOnBriefLandscapeBlip()
    {
        // One landscape sample is a blip, not a rotation - the dwell is what tells them apart.
        var d = new FlipToTalkDetector();
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(60)).Should().BeFalse();
        d.Process(Portrait(120)).Should().BeFalse();
    }

    [Fact]
    public void FlipDoesNotFireOnLateralJolt()
    {
        // An arm swing pushes |X| past the dominance threshold while |a| is nowhere near 1g:
        // that's hand acceleration, not gravity, so it must not read as landscape.
        var d = new FlipToTalkDetector();
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(new SensorSample(At(60), -1.4f, -1.1f, 0.3f)).Should().BeFalse();
        d.Process(new SensorSample(At(120), -2.2f, -0.6f, 0.2f)).Should().BeFalse();
        d.Process(new SensorSample(At(180), -1.5f, -0.9f, 0.1f)).Should().BeFalse();
        d.Process(new SensorSample(At(240), -1.2f, -1.3f, 0.2f)).Should().BeFalse();
        d.Process(Portrait(300)).Should().BeFalse();
        d.Process(Portrait(360)).Should().BeFalse();
    }

    [Fact]
    public void FlipDoesNotFireWhenReturnExceedsWindow()
    {
        var d = new FlipToTalkDetector();
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Landscape(600)).Should().BeFalse();
        d.Process(Portrait(4000)).Should().BeFalse();
    }

    [Fact]
    public void FlipDoesNotFireThroughFlat()
    {
        var d = new FlipToTalkDetector();
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(200)).Should().BeFalse();
        d.Process(Landscape(500)).Should().BeFalse();
        d.Process(FaceUp(700)).Should().BeFalse();
        d.Process(Portrait(900)).Should().BeFalse();
    }

    [Fact]
    public void ShakeFiresOnAlternatingSpikes()
    {
        // act + assert
        HasFired(Rest(0).Concat(Burst(1.0f, extremes: 4, startMs: 1000, stepMs: 80)), ShakeSensitivity.Medium)
            .Should().BeTrue();
    }

    [Fact]
    public void ShakeDoesNotFireOnSingleSpike()
    {
        var spike = new[] { new SensorSample(At(1000), 3f, 0f, 1f) };

        // act + assert
        HasFired(Rest(0).Concat(spike).Concat(Rest(1060)), ShakeSensitivity.Medium).Should().BeFalse();
    }

    [Fact]
    public void ShakeDoesNotFireWhenSpikesAreTooSlow()
    {
        // act + assert
        HasFired(Rest(0).Concat(Burst(1.0f, extremes: 4, startMs: 1000, stepMs: 400)), ShakeSensitivity.Medium)
            .Should().BeFalse();
    }

    [Fact]
    public void ShakeHonoursDebounce()
    {
        // The sensor feed is continuous, so the bursts ride on rest samples, not silence.
        var samples = Rest(0)
            .Concat(Burst(1.2f, extremes: 4, startMs: 1000, stepMs: 70))
            .Concat(Burst(1.2f, extremes: 4, startMs: 1500, stepMs: 70))
            .Concat(Rest(1780, durationMs: 1220))
            .Concat(Burst(1.2f, extremes: 4, startMs: 3000, stepMs: 70));

        // act + assert: the second burst starts inside the 1s debounce of the first fire
        CountFires(samples, ShakeSensitivity.High).Should().Be(2);
    }

    [Theory]
    [InlineData(ShakeSensitivity.Low)]
    [InlineData(ShakeSensitivity.Medium)]
    public void ShakeSensitivityIsMonotonic(ShakeSensitivity fired)
    {
        // Anything that fires at a lower sensitivity must also fire at a higher one.
        var amplitude = ShakeDetector.GetDeviationThreshold(fired) + 0.2f;
        var extremes = ShakeDetector.GetReversalCount(fired) + 1;
        var stronger = fired == ShakeSensitivity.Low ? ShakeSensitivity.Medium : ShakeSensitivity.High;
        var samples = Rest(0).Concat(Burst(amplitude, extremes, startMs: 1000, stepMs: 70)).ToArray();

        // act + assert
        HasFired(samples, fired).Should().BeTrue();
        HasFired(samples, stronger).Should().BeTrue();
    }

    [Fact]
    public void ShakeDoesNotFireOnFlipRotation()
    {
        // A full flip rotates gravity portrait -> landscape -> portrait: each axis sees one
        // monotone swing per leg, so the high-passed signal produces at most one reversal -
        // never the two that even High sensitivity demands.
        var samples = new List<SensorSample>();
        for (var t = 0d; t < 1000; t += 60)
            samples.Add(Portrait(t));
        for (var i = 1; i <= 6; i++) {
            var angle = i * MathF.PI / 12f;
            samples.Add(new SensorSample(At(1000 + (i * 60)), -MathF.Sin(angle), -MathF.Cos(angle), 0f));
        }
        for (var t = 1420d; t < 1900; t += 60)
            samples.Add(Landscape(t));
        for (var i = 1; i <= 6; i++) {
            var angle = (6 - i) * MathF.PI / 12f;
            samples.Add(new SensorSample(At(1900 + (i * 60)), -MathF.Sin(angle), -MathF.Cos(angle), 0f));
        }
        for (var t = 2320d; t < 3000; t += 60)
            samples.Add(Portrait(t));

        // act + assert
        HasFired(samples, ShakeSensitivity.High).Should().BeFalse();
    }

    [Fact]
    public void FaceDownUsesTheRealMauiZConvention()
    {
        // On both platforms MAUI yields Z ≈ +1 for a device lying flat face-UP
        // (Android divides raw +9.81 by gravity; iOS negates CoreMotion's -1).
        var d = new FaceDownDetector();
        // act + assert
        d.Process(new SensorSample(At(0), 0f, 0f, 1f)).Should().BeFalse();
        d.Process(new SensorSample(At(1200), 0f, 0f, 1f)).Should().BeFalse();
        d.Process(new SensorSample(At(2000), 0f, 0f, -1f)).Should().BeFalse();
        d.Process(new SensorSample(At(3000), 0f, 0f, -1f)).Should().BeTrue();
    }

    [Fact]
    public void FaceDownFiresAfterDwell()
    {
        var d = new FaceDownDetector();
        // act + assert
        d.Process(FaceDown(0)).Should().BeFalse();
        d.Process(FaceDown(400)).Should().BeFalse();
        d.Process(FaceDown(1200)).Should().BeTrue();
    }

    [Fact]
    public void FaceDownDoesNotFireOnTransientPickUp()
    {
        var d = new FaceDownDetector();
        // act + assert
        d.Process(FaceDown(0)).Should().BeFalse();
        d.Process(FaceDown(200)).Should().BeFalse();
        d.Process(Portrait(400)).Should().BeFalse();
        d.Process(FaceDown(600)).Should().BeFalse();
    }

    [Fact]
    public void FaceDownFiresOnCoveredAndUpright()
    {
        var d = new FaceDownDetector();
        d.SetProximityCovered(true);
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Portrait(1200)).Should().BeTrue();
    }

    [Fact]
    public void FaceDownUprightAloneDoesNotFire()
    {
        var d = new FaceDownDetector();
        // act + assert
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Portrait(5000)).Should().BeFalse();
    }

    [Fact]
    public void RecognizerRoutesOnlyToEnabledDetectors()
    {
        var options = new GestureOptions(false, true, true, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        // act + assert
        r.Process(Portrait(0));
        r.Process(Landscape(300));
        r.Process(Portrait(600)).Should().BeNull();
    }

    [Fact]
    public void RecognizerStopBeatsStart()
    {
        var options = new GestureOptions(true, true, true, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        r.SetProximityCovered(true);
        // Face-down (700ms dwell since t=0) completes on the last sample while the same burst
        // feeds the shake detector - the recognizer must report the stop, never a start, because
        // GestureRecognizer.Process evaluates face-down first.
        // act + assert
        r.Process(new SensorSample(At(0), 0f, 0f, -3f)).Should().BeNull();
        r.Process(new SensorSample(At(600), 0f, 0f, -1f)).Should().BeNull();
        r.Process(new SensorSample(At(650), 0f, 0f, -0.2f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(700), 0f, 0f, -3f));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void ShakeAmplitude06FiresOnlyAtHigh()
    {
        var samples = Rest(0).Concat(Burst(0.6f, extremes: 5, startMs: 1000, stepMs: 70)).ToArray();

        // act + assert
        HasFired(samples, ShakeSensitivity.Low).Should().BeFalse();
        HasFired(samples, ShakeSensitivity.Medium).Should().BeFalse();
        HasFired(samples, ShakeSensitivity.High).Should().BeTrue();
    }

    [Fact]
    public void ShakeAmplitude09FiresAtMediumAndHigh()
    {
        var samples = Rest(0).Concat(Burst(0.9f, extremes: 5, startMs: 1000, stepMs: 70)).ToArray();

        // act + assert
        HasFired(samples, ShakeSensitivity.Low).Should().BeFalse();
        HasFired(samples, ShakeSensitivity.Medium).Should().BeTrue();
        HasFired(samples, ShakeSensitivity.High).Should().BeTrue();
    }

    [Fact]
    public void ShakeAmplitude14FiresAtAllSensitivities()
    {
        var samples = Rest(0).Concat(Burst(1.4f, extremes: 5, startMs: 1000, stepMs: 70)).ToArray();

        // act + assert
        HasFired(samples, ShakeSensitivity.Low).Should().BeTrue();
        HasFired(samples, ShakeSensitivity.Medium).Should().BeTrue();
        HasFired(samples, ShakeSensitivity.High).Should().BeTrue();
    }

    [Fact]
    public void ShakeSensitivityNestsAcrossRandomSequences()
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

            // act + assert
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
    public void ShakeChangeSensitivityPreservesDebounceAndPeak()
    {
        var d = new ShakeDetector(ShakeSensitivity.High);

        // act + assert
        var hasFired = false;
        foreach (var sample in Rest(0).Concat(Burst(0.7f, extremes: 3, startMs: 1000, stepMs: 60)))
            hasFired |= d.Process(sample);
        hasFired.Should().BeTrue();
        var peakBeforeChange = d.PeakDeviation;
        peakBeforeChange.Should().BeGreaterThan(0f);
        d.ChangeSensitivity(ShakeSensitivity.Low);
        d.PeakDeviation.Should().Be(peakBeforeChange);
        // The debounce set by the High-sensitivity fire must still block a fresh Low-sensitivity
        // burst that starts well inside the debounce window.
        hasFired = false;
        foreach (var sample in Burst(1.4f, extremes: 5, startMs: 1500, stepMs: 60))
            hasFired |= d.Process(sample);
        hasFired.Should().BeFalse();
    }

    [Fact]
    public void RecognizerEnableToggleResetsFaceDownDwell()
    {
        var options = new GestureOptions(false, false, true, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        // act + assert
        r.Process(new SensorSample(At(0), 0f, 0f, -1f)).Should().BeNull();
        r.Process(new SensorSample(At(400), 0f, 0f, -1f)).Should().BeNull();
        r.Options = options with { IsFaceDownEnabled = false };
        r.Options = options with { IsFaceDownEnabled = true };
        // Without the reset-on-toggle fix, the stale _heldSince(0) would make this look like the
        // dwell already elapsed (1000ms since t=0), firing immediately.
        r.Process(new SensorSample(At(1000), 0f, 0f, -1f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(1750), 0f, 0f, -1f));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void RecognizerSampleGapResetsFaceDownDwell()
    {
        var options = new GestureOptions(false, false, true, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        // act + assert
        r.Process(new SensorSample(At(0), 0f, 0f, -1f)).Should().BeNull();
        r.Process(new SensorSample(At(400), 0f, 0f, -1f)).Should().BeNull();
        // A 2.5s gap simulates the app being backgrounded and resumed - without the gap guard,
        // the stale _heldSince(0) would make this look like the dwell already elapsed.
        r.Process(new SensorSample(At(2900), 0f, 0f, -1f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(3650), 0f, 0f, -1f));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void ShakeFiresOnHorizontalShake()
    {
        // The real-device shake: phone flat-ish (gravity on Z), oscillation along X at ~3.6Hz
        // reaching ±1.5g, sampled at SensorSpeed.UI's ~60ms cadence. Total-magnitude schemes
        // are blind to this geometry - sqrt(1 + x²) never drops below 1g.
        // act + assert
        HasFired(Oscillation(GravityAxis.X, 1.5f), ShakeSensitivity.Medium).Should().BeTrue();
    }

    private static IEnumerable<SensorSample> Oscillation(
        GravityAxis axis, float amplitude, double startMs = 0, int halfCycles = 6,
        double stepMs = 60, double periodMs = 280)
    {
        // 1s of stillness first, so the detector's gravity estimate settles before the burst.
        for (var t = 0d; t < 1000; t += stepMs)
            yield return new SensorSample(At(startMs + t), 0f, 0f, 1f);

        for (var t = 0d; t <= halfCycles * periodMs / 2; t += stepMs) {
            var v = amplitude * MathF.Sin((float)(2 * Math.PI * t / periodMs));
            var at = At(startMs + 1000 + t);
            yield return axis switch {
                GravityAxis.X => new SensorSample(at, v, 0f, 1f),
                GravityAxis.Y => new SensorSample(at, 0f, v, 1f),
                _ => new SensorSample(at, 0f, 0f, 1f + v),
            };
        }
    }

    private static bool HasFired(IEnumerable<SensorSample> samples, ShakeSensitivity sensitivity)
    {
        var detector = new ShakeDetector(sensitivity);
        var hasFired = false;
        foreach (var sample in samples)
            hasFired |= detector.Process(sample);
        return hasFired;
    }

    private static int CountFires(IEnumerable<SensorSample> samples, ShakeSensitivity sensitivity)
    {
        var detector = new ShakeDetector(sensitivity);
        var fires = 0;
        foreach (var sample in samples)
            if (detector.Process(sample))
                fires++;
        return fires;
    }

    private static IEnumerable<SensorSample> Rest(double startMs, double durationMs = 1000, double stepMs = 60)
    {
        for (var t = 0d; t < durationMs; t += stepMs)
            yield return new SensorSample(At(startMs + t), 0f, 0f, 1f);
    }

    private static IEnumerable<SensorSample> Burst(
        float amplitude, int extremes, double startMs, double stepMs)
    {
        // Alternating lateral extremes with gravity still on Z - the geometry a hand shake
        // actually produces.
        for (var i = 0; i < extremes; i++) {
            var x = i % 2 == 0 ? amplitude : -amplitude;
            yield return new SensorSample(At(startMs + (i * stepMs)), x, 0f, 1f);
        }
    }
}
