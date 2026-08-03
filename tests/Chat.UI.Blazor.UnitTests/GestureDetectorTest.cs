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
    public void Flip_DoesNotFireOnHalfRotation()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Landscape(5000)).Should().BeFalse();
    }

    [Fact]
    public void Flip_DoesNotFireWhenReturnExceedsWindow()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Portrait(4000)).Should().BeFalse();
    }

    [Fact]
    public void Flip_DoesNotFireThroughFlat()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(200)).Should().BeFalse();
        d.Process(FaceUp(400)).Should().BeFalse();
        d.Process(Portrait(600)).Should().BeFalse();
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
        // A shake that ends face-down must report the stop, never the start.
        r.Process(FaceDown(0));
        r.Process(new SensorSample(At(60), 0f, 0f, 3f));
        r.Process(new SensorSample(At(120), 0f, 0f, -2f));
        r.Process(new SensorSample(At(180), 0f, 0f, 3f));
        var e = r.Process(FaceDown(1500));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
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
