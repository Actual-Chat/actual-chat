using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class GestureDetectorTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    // MAUI yields Y ≈ +1 for an UPRIGHT portrait (verified on-device 2026-08-11); the flip and
    // face-down detectors are Y-sign-agnostic, but the upside-down pocket latch is not.
    private static SensorSample Portrait(double atMs) => new(At(atMs), 0f, 1f, 0f);
    private static SensorSample Landscape(double atMs) => new(At(atMs), -1f, 0f, 0f);
    private static SensorSample FaceUp(double atMs) => new(At(atMs), 0f, 0f, 1f);
    private static SensorSample FaceDown(double atMs) => new(At(atMs), 0f, 0f, -1f);
    private static SensorSample FaceDownJittery(double atMs) => new(At(atMs), 0.6f, 0f, -0.95f);
    private static SensorSample UpsideDown(double atMs) => new(At(atMs), 0f, -1f, 0f);
    private static Moment At(double ms) => T0 + TimeSpan.FromMilliseconds(ms);

    private static IEnumerable<SensorSample> PocketRest(double startMs, double durationMs = 1000, double stepMs = 20)
    {
        for (var t = startMs; t < startMs + durationMs; t += stepMs)
            yield return UpsideDown(t);
    }

    private static SensorSample Pat(double atMs, float g = 1.2f) => new(At(atMs), 0f, -1f, g);

    // A turn by `degrees` around the screen normal, spread evenly over `durationMs` at 50Hz
    private static IEnumerable<GyroSample> Turn(double startMs, float degrees, double durationMs)
    {
        var count = Math.Max(1, (int)(durationMs / 20));
        var delta = degrees * MathF.PI / 180f / count;
        var rate = delta / 0.02f;
        for (var i = 1; i <= count; i++)
            yield return new GyroSample(At(startMs + (i * 20)), 0f, 0f, rate, delta);
    }

    private static bool FlipFired(IEnumerable<GyroSample> samples, (float X, float Y, float Z)? gravity = null)
    {
        var d = new FlipToTalkDetector();
        var g = gravity ?? (0f, 1f, 0f);
        return samples.Aggregate(false, (fired, s) => d.Process(s, g) || fired);
    }

    private static bool PatFired(IEnumerable<SensorSample> samples)
    {
        var d = new PatDetector();
        return samples.Aggregate(false, (fired, s) => d.Process(s) || fired);
    }

    [Fact]
    public void FlipFiresOnATurnAndBack()
    {
        // arrange
        var turn = Turn(0, 80, 200).Concat(Turn(200, 0, 200)).Concat(Turn(400, -80, 200));

        // act + assert
        FlipFired(turn).Should().BeTrue();
    }

    [Fact]
    public void FlipFiresOnASlowTurn()
    {
        // arrange: a turn taken at leisure - 0.7s each way, held 0.8s
        var turn = Turn(0, 70, 700).Concat(Turn(700, 0, 800)).Concat(Turn(1500, -70, 700));

        // act + assert
        FlipFired(turn).Should().BeTrue();
    }

    [Fact]
    public void FlipFiresWhileTheHandMoves()
    {
        // arrange: rotation is all the gyroscope reports, so a hand moving the phone doesn't matter -
        // here the turn arrives in uneven jerks
        var turn = Turn(0, 30, 40).Concat(Turn(40, 5, 100)).Concat(Turn(140, 45, 60))
            .Concat(Turn(200, 0, 150)).Concat(Turn(350, -50, 60)).Concat(Turn(410, -30, 120));

        // act + assert
        FlipFired(turn).Should().BeTrue();
    }

    [Fact]
    public void FlipDoesNotFireOnAHalfTurn()
        => FlipFired(Turn(0, 80, 200).Concat(Turn(200, 0, 2000))).Should().BeFalse();

    [Fact]
    public void FlipDoesNotFireOnSmallTwists()
    {
        // arrange: a wrist twisting back and forth by 40 degrees
        var twists = Enumerable.Range(0, 6).SelectMany(i => Turn(i * 200, i % 2 == 0 ? 40 : -40, 200));

        // act + assert
        FlipFired(twists).Should().BeFalse();
    }

    [Fact]
    public void FlipDoesNotFireOnAFastWristShake()
    {
        // arrange: past the turn and straight back within ~40ms
        var shake = Turn(0, 65, 40).Concat(Turn(40, -65, 40));

        // act + assert
        FlipFired(shake).Should().BeFalse();
    }

    [Fact]
    public void FlipDoesNotFireWhenTheReturnIsTooLate()
        => FlipFired(Turn(0, 80, 200).Concat(Turn(200, 0, 2600)).Concat(Turn(2800, -80, 200)))
            .Should().BeFalse("a turn held that long is the phone's new position");

    [Fact]
    public void FlipDoesNotFireOnAPhoneSpunWhileLyingFlat()
    {
        // arrange
        var turn = Turn(0, 80, 200).Concat(Turn(200, 0, 200)).Concat(Turn(400, -80, 200));

        // act + assert
        FlipFired(turn, gravity: (0f, 0f, 1f)).Should().BeFalse();
    }

    [Fact]
    public void ShakeFiresOnAlternatingSpikes()
    {
        // act + assert
        HasFired(Rest(0).Concat(Burst(1.0f, extremes: 4, startMs: 1000, stepMs: 80)), ShakeSensitivity.Medium)
            .Should().BeTrue();
    }

    [Fact]
    public void ShakeIgnoresJoltsThroughTheScreen()
    {
        // arrange: alternating jolts along Z are pats, not a shake
        var samples = Rest(0).Concat(Enumerable.Range(0, 6)
            .Select(i => new SensorSample(At(1000 + (i * 80)), 0f, 0f, i % 2 == 0 ? 2.2f : -0.2f)));

        // act + assert
        HasFired(samples, ShakeSensitivity.High).Should().BeFalse();
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
        d.SetProximityCovered(true);
        // act + assert
        d.Process(new SensorSample(At(0), 0f, 0f, 1f)).Should().Be(GestureKind.None);
        d.Process(new SensorSample(At(1200), 0f, 0f, 1f)).Should().Be(GestureKind.None);
        d.Process(new SensorSample(At(1400), 0f, 0f, -1f)).Should().Be(GestureKind.None);
        d.Process(new SensorSample(At(2300), 0f, 0f, -1f)).Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void FaceDownAlreadyHeldStillAndCoveredFiresAfterStillDwell()
    {
        // Armed while already face-down on a desk = slow entry, so plain dwell isn't enough;
        // still + proximity-covered satisfies the slow path at StillDwell (1200ms).
        var d = new FaceDownDetector();
        d.SetProximityCovered(true);
        // act + assert
        d.Process(FaceDown(0)).Should().Be(GestureKind.None);
        d.Process(FaceDown(400)).Should().Be(GestureKind.None);
        d.Process(FaceDown(1200)).Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void FaceDownAlreadyHeldStillButUncoveredStaysSilent()
    {
        // The in-bed overhead hold can be as still as a desk (braced arm), but nothing
        // touches the glass - without proximity coverage the slow path must never fire.
        var d = new FaceDownDetector();
        // act + assert
        d.Process(FaceDown(0)).Should().Be(GestureKind.None);
        d.Process(FaceDown(400)).Should().Be(GestureKind.None);
        d.Process(FaceDown(1200)).Should().Be(GestureKind.None);
        d.Process(FaceDown(5000)).Should().Be(GestureKind.None);
    }

    [Fact]
    public void FaceDownDoesNotFireOnTransientPickUp()
    {
        var d = new FaceDownDetector();
        d.SetProximityCovered(true);
        // act + assert
        d.Process(FaceDown(0)).Should().Be(GestureKind.None);
        d.Process(FaceDown(200)).Should().Be(GestureKind.None);
        d.SetProximityCovered(false);
        d.Process(Portrait(400)).Should().Be(GestureKind.None);
        d.SetProximityCovered(true);
        d.Process(FaceDown(600)).Should().Be(GestureKind.None);
    }

    [Fact]
    public void PocketShouldNotFireWhenCoveredAndUpright()
    {
        // The phone held to the ear: covered and near-vertical, but top up (#4711).
        var d = new FaceDownDetector();
        d.SetProximityCovered(true);
        // act + assert
        d.Process(Portrait(0)).Should().Be(GestureKind.None);
        d.Process(Portrait(1200)).Should().Be(GestureKind.None);
        d.Process(new SensorSample(At(2400), 0.5f, 0.8f, 0.2f)).Should().Be(GestureKind.None);
    }

    [Fact]
    public void PocketShouldFireWhenCoveredAndUpsideDown()
    {
        // The taught pocket gesture - turn the phone over and slide it away.
        var d = new FaceDownDetector();
        d.SetProximityCovered(true);
        // act + assert
        d.Process(UpsideDown(0)).Should().Be(GestureKind.None);
        d.Process(UpsideDown(1200)).Should().Be(GestureKind.Pocket);
    }

    [Fact]
    public void PocketShouldNotFireWhenUprightAndUncovered()
    {
        var d = new FaceDownDetector();
        // act + assert
        d.Process(Portrait(0)).Should().Be(GestureKind.None);
        d.Process(Portrait(5000)).Should().Be(GestureKind.None);
    }

    [Fact]
    public void FaceDownShouldNotFireOnAFastFlipInTheAir()
    {
        // A flip that lands on nothing leaves the glass uncovered. It used to fire on dwell
        // alone - the mid-air false stop the proximity requirement removes.
        var d = new FaceDownDetector();
        // act + assert
        d.Process(FaceUp(0)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(200)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(400)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(900)).Should().Be(GestureKind.None);
    }

    [Fact]
    public void FaceDownShouldFireOnAFastFlipOntoASurfaceDespiteHandTremor()
    {
        var d = new FaceDownDetector();
        // act + assert
        d.Process(FaceUp(0)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(200)).Should().Be(GestureKind.None);
        d.SetProximityCovered(true);
        d.Process(FaceDownJittery(400)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(900)).Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void FaceDownShouldFireAsSoonAsAHeldFlipLands()
    {
        // The dwell keeps running while uncovered, so a phone flipped over in the hand and
        // then set down fires on landing instead of restarting its 700ms on the surface.
        var d = new FaceDownDetector();
        // act + assert
        d.Process(FaceUp(0)).Should().Be(GestureKind.None);
        d.Process(FaceDown(200)).Should().Be(GestureKind.None);
        d.Process(FaceDown(2000)).Should().Be(GestureKind.None);
        d.SetProximityCovered(true);
        d.Process(FaceDown(2100)).Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void FaceDownDoesNotFireOnSlowReclineWithPhoneInHand()
    {
        // Reclining onto a bed rotates the phone into face-down slowly, and the hand holding
        // it overhead never goes still - so neither the fast-entry path nor the stillness
        // path may fire, however long the posture lasts.
        var d = new FaceDownDetector();
        // act + assert
        d.Process(Portrait(0)).Should().Be(GestureKind.None);
        d.Process(new SensorSample(At(600), 0f, 0.98f, -0.2f)).Should().Be(GestureKind.None);
        d.Process(new SensorSample(At(900), 0f, 0.8f, -0.6f)).Should().Be(GestureKind.None);
        for (var atMs = 1200; atMs <= 10200; atMs += 600)
            d.Process(FaceDownJittery(atMs)).Should().Be(GestureKind.None, $"held overhead at {atMs}ms");
    }

    [Fact]
    public void FaceDownSlowEntryFiresAfterSettlingStillOnCoveredSurface()
    {
        // A gentle table placement can rotate in slowly too - what tells it apart from the
        // reclined hold is the glass landing on the surface: still AND covered fires.
        var d = new FaceDownDetector();
        // act + assert
        d.Process(Portrait(0)).Should().Be(GestureKind.None);
        d.Process(new SensorSample(At(600), 0f, 0.98f, -0.2f)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(1200)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(1800)).Should().Be(GestureKind.None);
        d.SetProximityCovered(true);
        d.Process(FaceDown(2400)).Should().Be(GestureKind.None);
        d.Process(FaceDown(3000)).Should().Be(GestureKind.None);
        d.Process(FaceDown(3600)).Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void FaceDownSlowEntryStillButUncoveredStaysSilent()
    {
        // The recorded in-bed false trigger: slow entry, then a braced arm holding the phone
        // overhead dead-still - stillness alone must not fire while nothing covers the glass.
        var d = new FaceDownDetector();
        // act + assert
        d.Process(Portrait(0)).Should().Be(GestureKind.None);
        d.Process(new SensorSample(At(600), 0f, 0.98f, -0.2f)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(1200)).Should().Be(GestureKind.None);
        for (var atMs = 1800; atMs <= 7800; atMs += 600)
            d.Process(FaceDown(atMs)).Should().Be(GestureKind.None, $"still overhead at {atMs}ms");
    }

    [Fact]
    public void FaceDownArmedWhileAlreadyOverheadStaysSilent()
    {
        // No not-face-down sample since Reset counts as a slow entry, so arming mid-recline
        // (gesture toggled on, app resumed) can't fire from the tremor-jittered hold alone.
        var d = new FaceDownDetector();
        // act + assert
        for (var atMs = 0; atMs <= 6000; atMs += 600)
            d.Process(FaceDownJittery(atMs)).Should().Be(GestureKind.None, $"held overhead at {atMs}ms");
    }

    [Fact]
    public void FaceDownShouldNotFireOnFlipUpAndBackWhileReclined()
    {
        // The old escape hatch for a reclined user - tilt up past NotFaceDownZ and flip back -
        // is gone with the proximity requirement: nothing covers the glass overhead. Shaking is
        // the stop gesture that works in the hand, see the recognizer's open-mic shake test.
        var d = new FaceDownDetector();
        // act + assert
        for (var atMs = 0; atMs <= 3000; atMs += 600)
            d.Process(FaceDownJittery(atMs)).Should().Be(GestureKind.None, $"held overhead at {atMs}ms");
        d.Process(Portrait(3200)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(3400)).Should().Be(GestureKind.None);
        d.Process(FaceDownJittery(4100)).Should().Be(GestureKind.None);
    }

    [Fact]
    public void FaceDownHasEnteredOnlyAfterANotFaceDownSample()
    {
        var d = new FaceDownDetector();
        d.SetProximityCovered(true);
        // act + assert: armed while already face down = no entry, however long it rests
        d.Process(FaceDown(0));
        d.Process(FaceDown(1300));
        d.HasEntered.Should().BeFalse();
        d.Reset();
        d.Process(FaceUp(0));
        d.Process(FaceDown(100));
        d.HasEntered.Should().BeTrue();
    }

    [Fact]
    public void DoublePatFiresOnTwoTapsInRhythm()
        => PatFired(PocketRest(0).Concat([Pat(1000)]).Concat(PocketRest(1050, 250)).Concat([Pat(1300)]).Concat(PocketRest(1350)))
            .Should().BeTrue();

    [Fact]
    public void DoublePatFiresWhenEachPatSpansTwoSamples()
    {
        // At the 50Hz cadence a firm pat lands on several consecutive samples, and its rebound can
        // cross the threshold as well; neither may read as a third impulse.
        var samples = PocketRest(0)
            .Concat([Pat(1000), Pat(1020)]).Concat(PocketRest(1040, 260)).Concat([Pat(1300), Pat(1320)])
            .Concat([new SensorSample(At(1340), 0f, -1f, -0.8f)])
            .Concat(PocketRest(1360));
        // act + assert
        PatFired(samples).Should().BeTrue();
    }

    [Fact]
    public void DoublePatIsDroppedWhenThePhoneTurnsDuringTheHold()
    {
        // arrange: pulled out of a pocket, the phone turns toward the face right after the "pats"
        var turning = Enumerable.Range(1, 30).Select(i => {
            var angle = MathF.Min(1f, i / 15f) * MathF.PI / 3;
            return new SensorSample(At(1300 + (i * 20)), 0f, -MathF.Cos(angle), MathF.Sin(angle));
        });
        var samples = PocketRest(0).Concat([Pat(1000)]).Concat(PocketRest(1020, 280)).Concat([Pat(1300)])
            .Concat(turning);

        // act + assert
        PatFired(samples).Should().BeFalse();
    }

    [Fact]
    public void DoublePatIsDroppedWhenLightRisesDuringTheHold()
    {
        // arrange
        var d = new PatDetector();
        var pair = PocketRest(0).Concat([Pat(1000)]).Concat(PocketRest(1020, 280)).Concat([Pat(1300)])
            .Concat(PocketRest(1320, 100));

        // act
        var firedEarly = pair.Count(d.Process);
        d.SetLit(At(1420));
        var firedLater = PocketRest(1420, 600).Count(d.Process);

        // assert
        (firedEarly + firedLater).Should().Be(0);
    }

    [Fact]
    public void JoltsAlongThePhoneDoNotReadAsPats()
    {
        // arrange: a step jolts a pocketed phone along its length, not through its screen
        static SensorSample Jolt(double atMs) => new(At(atMs), 0f, -2.2f, 0f);
        var samples = PocketRest(0).Concat([Jolt(1000)]).Concat(PocketRest(1020, 280)).Concat([Jolt(1300)])
            .Concat(PocketRest(1320));

        // act + assert
        PatFired(samples).Should().BeFalse();
    }

    [Fact]
    public void SinglePatDoesNotFire()
        => PatFired(PocketRest(0).Concat([Pat(1000)]).Concat(PocketRest(1050, 2000)))
            .Should().BeFalse();

    [Fact]
    public void AThirdTapInsideTheWindowCancelsThePair()
        => PatFired(PocketRest(0).Concat([Pat(1000)]).Concat(PocketRest(1050, 250)).Concat([Pat(1300)])
                .Concat(PocketRest(1350, 250)).Concat([Pat(1600)])
                .Concat(PocketRest(1650)))
            .Should().BeFalse("three impulses in a row is a bump train, not a double-pat");

    [Fact]
    public void TapsTooCloseOrTooFarApartDoNotFire()
    {
        // act + assert
        PatFired(PocketRest(0).Concat([Pat(1000), Pat(1050)]).Concat(PocketRest(1100)))
            .Should().BeFalse("50ms apart is one impulse spread over two samples");
        PatFired(PocketRest(0).Concat([Pat(1000)]).Concat(PocketRest(1050, 700)).Concat([Pat(1800)])
                .Concat(PocketRest(1850)))
            .Should().BeFalse("800ms apart is two separate bumps");
    }

    [Fact]
    public void GaitLikePeriodicImpulsesDoNotFire()
    {
        // Steps at ~2Hz never leave the 400ms quiet the first tap of a pat needs.
        var samples = new List<SensorSample>();
        for (var t = 0.0; t < 6000; t += 50)
            samples.Add(t % 500 < 50 ? Pat(t, 1.6f) : UpsideDown(t));
        // act + assert
        PatFired(samples).Should().BeFalse();
    }

    [Fact]
    public void DoublePatHonoursDebounce()
    {
        var d = new PatDetector();
        var samples = PocketRest(0)
            .Concat([Pat(1000)]).Concat(PocketRest(1050, 250)).Concat([Pat(1300)]).Concat(PocketRest(1350, 600))
            .Concat([Pat(1950)]).Concat(PocketRest(2000, 250)).Concat([Pat(2250)]).Concat(PocketRest(2300))
            .Concat([Pat(3400)]).Concat(PocketRest(3450, 250)).Concat([Pat(3700)]).Concat(PocketRest(3750));
        // act + assert: the second pair starts inside the 1s debounce of the first fire
        samples.Count(d.Process).Should().Be(2);
    }

    [Fact]
    public void RecognizerRoutesOnlyToEnabledDetectors()
    {
        var options = new GestureOptions(false, true, true, false, false, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        // act + assert
        r.Process(Portrait(0));
        r.Process(Landscape(300));
        r.Process(Portrait(600)).Should().BeNull();
    }

    [Fact]
    public void RecognizerStopBeatsStart()
    {
        var options = new GestureOptions(true, true, true, false, false, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        r.SetProximityCovered(true);
        // Face-down enters fast at t=200 and completes its 700ms dwell on the last sample
        // while the same burst feeds the shake detector - the recognizer must report the
        // stop, never a start, because GestureRecognizer.Process evaluates face-down first.
        // act + assert
        r.Process(new SensorSample(At(0), 0f, 0f, 1f)).Should().BeNull();
        r.Process(new SensorSample(At(200), 0f, 0f, -3f)).Should().BeNull();
        r.Process(new SensorSample(At(800), 0f, 0f, -1f)).Should().BeNull();
        r.Process(new SensorSample(At(850), 0f, -0.6f, -0.2f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(900), 0f, 0f, -3f));
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
        var options = new GestureOptions(false, false, true, false, false, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        r.SetProximityCovered(true);
        // act + assert
        r.Process(new SensorSample(At(0), 0f, 0f, -1f)).Should().BeNull();
        r.Process(new SensorSample(At(400), 0f, 0f, -1f)).Should().BeNull();
        r.Options = options with { IsStopGestureEnabled = false };
        r.Options = options with { IsStopGestureEnabled = true };
        r.Process(new SensorSample(At(1000), 0f, 0f, -1f)).Should().BeNull();
        // Without the reset-on-toggle fix, the stale _stillSince(0) would satisfy StillDwell
        // here (1750ms of stillness since t=0), making this sample fire.
        r.Process(new SensorSample(At(1750), 0f, 0f, -1f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(2200), 0f, 0f, -1f));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void RecognizerSampleGapResetsFaceDownDwell()
    {
        var options = new GestureOptions(false, false, true, false, false, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        r.SetProximityCovered(true);
        // act + assert
        r.Process(new SensorSample(At(0), 0f, 0f, -1f)).Should().BeNull();
        r.Process(new SensorSample(At(400), 0f, 0f, -1f)).Should().BeNull();
        // A 2.5s gap simulates the app being backgrounded and resumed - without the gap
        // guard, the stale _stillSince(0) would satisfy StillDwell, firing right here.
        r.Process(new SensorSample(At(2900), 0f, 0f, -1f)).Should().BeNull();
        var e = r.Process(new SensorSample(At(4100), 0f, 0f, -1f));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    [Fact]
    public void RecognizerCoveredSuppressesShake()
    {
        var options = new GestureOptions(false, true, false, false, false, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        r.SetProximityCovered(true);
        // act + assert: upright, i.e. pocket-like - a phone resting screen-up is covered by
        // RecognizerCoverDoesNotSuppressWhileRestingScreenUp instead.
        foreach (var sample in RestUpright(0).Concat(BurstUpright(1.4f, extremes: 5, startMs: 1000, stepMs: 70)))
            r.Process(sample).Should().BeNull("covered carry must suppress shake");
    }

    [Fact]
    public void RecognizerCoveredSuppressesFlip()
    {
        // arrange
        var r = new GestureRecognizer(new GestureOptions(true, false, false, false, false, ShakeSensitivity.Medium));
        r.SetProximityCovered(true);
        foreach (var s in Enumerable.Range(0, 40).Select(i => Portrait(i * 20)))
            r.Process(s);
        var turn = Turn(800, 80, 200).Concat(Turn(1000, 0, 200)).Concat(Turn(1200, -80, 200));

        // act + assert
        turn.Select(r.ProcessGyro).Should().AllSatisfy(e => e.Should().BeNull("covered carry must suppress flip"));
    }

    [Fact]
    public void RecognizerFiresAFlipFromTheGyroscope()
    {
        // arrange
        var r = new GestureRecognizer(new GestureOptions(true, false, false, false, false, ShakeSensitivity.Medium));
        foreach (var s in Enumerable.Range(0, 40).Select(i => Portrait(i * 20)))
            r.Process(s);
        var turn = Turn(800, 80, 200).Concat(Turn(1000, 0, 200)).Concat(Turn(1200, -80, 200));

        // act + assert
        turn.Select(r.ProcessGyro).Should().Contain(e => e != null && e.Value.Kind == GestureKind.FlipToTalk);
    }

    [Fact]
    public void FlipProgressRisesThroughTheTurnAndClearsOnFire()
    {
        // arrange
        var d = new FlipToTalkDetector();
        var upright = (0f, 1f, 0f);

        // act + assert
        foreach (var s in Turn(0, 45, 100))
            d.Process(s, upright);
        d.GetProgress(At(100)).Should().BeApproximately(0.25f, 0.02f);
        foreach (var s in Turn(100, 45, 100))
            d.Process(s, upright);
        d.GetProgress(At(200)).Should().BeApproximately(0.5f, 0.02f);
        Turn(200, -90, 200).Any(s => d.Process(s, upright)).Should().BeTrue();
        d.GetProgress(At(400)).Should().Be(0f);
    }

    [Fact]
    public void RecognizerUpsideDownLatchSuppressesShake()
    {
        // Top-down pocket carry: gravity-dominated Y=-1 samples latch the guard; the walk
        // bounce (|a| far from 1g, so never gravity-dominated) cannot unlatch it.
        var options = new GestureOptions(false, true, false, false, false, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        // act + assert
        r.Process(UpsideDown(0)).Should().BeNull();
        r.Process(UpsideDown(200)).Should().BeNull();
        r.Process(UpsideDown(400)).Should().BeNull();
        foreach (var sample in Burst(1.4f, extremes: 6, startMs: 600, stepMs: 70))
            r.Process(sample).Should().BeNull("upside-down carry must suppress shake");
    }

    [Fact]
    public void GuardSuppressingReflectsTheUpsideDownLatch()
    {
        var options = new GestureOptions(false, false, false, false, false, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        r.IsGuardSuppressing.Should().BeFalse();

        // act: upside-down held past UpsideDownHold latches the guard
        r.Process(UpsideDown(0));
        r.Process(UpsideDown(200));
        r.Process(UpsideDown(400));

        // assert
        r.IsGuardSuppressing.Should().BeTrue();

        // act: upright held past UpsideDownHold clears the latch
        r.Process(Portrait(600));
        r.Process(Portrait(800));
        r.Process(Portrait(1000));
        r.Process(Portrait(1200));
        r.Process(Portrait(1400));

        // assert
        r.IsGuardSuppressing.Should().BeFalse();
    }

    [Fact]
    public void RecognizerUprightClearsUpsideDownLatchAndShakeFires()
    {
        var options = new GestureOptions(false, true, false, false, false, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        // act
        r.Process(UpsideDown(0));
        r.Process(UpsideDown(200));
        r.Process(UpsideDown(400));
        r.Process(Portrait(600));
        r.Process(Portrait(800));
        r.Process(Portrait(1000));
        var hasFired = false;
        foreach (var sample in Burst(1.4f, extremes: 6, startMs: 1200, stepMs: 70))
            hasFired |= r.Process(sample) is { Kind: GestureKind.DoubleShake };
        // assert
        hasFired.Should().BeTrue("an upright gravity-dominated sample clears the latch");
    }

    [Fact]
    public void RecognizerShouldFireAShakeWithAnOpenMicDespiteSuppression()
    {
        // A shake means "stop" while the mic is open, so it has to survive the pocket guard -
        // which exists to stop a jostle from OPENING a mic, not from closing one.
        var options = new GestureOptions(false, true, false, true, false, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        // act
        r.Process(UpsideDown(0));
        r.Process(UpsideDown(200));
        var hasFired = false;
        foreach (var sample in Burst(1.4f, extremes: 6, startMs: 400, stepMs: 70))
            hasFired |= r.Process(sample) is { Kind: GestureKind.DoubleShake };

        // assert
        hasFired.Should().BeTrue("the stop shake runs ahead of the pocket guard");
    }

    [Fact]
    public void RecognizerPocketStopFiresWhileStartGesturesSuppressed()
    {
        var options = new GestureOptions(true, true, true, false, false, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        r.SetProximityCovered(true);
        // act + assert
        r.Process(UpsideDown(0)).Should().BeNull();
        var e = r.Process(UpsideDown(1200));
        e!.Value.Kind.Should().Be(GestureKind.Pocket, "the pocket stop branch is never suppressed");
    }

    [Fact]
    public void RecognizerCoverDoesNotSuppressWhileRestingScreenUp()
    {
        var options = new GestureOptions(false, true, false, false, false, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        // act: a phone flat on a desk reads "near" on some devices, but it isn't pocketed
        r.SetProximityCovered(true);
        r.Process(FaceUp(0)).Should().BeNull();
        var hasFired = false;
        foreach (var sample in Burst(1.4f, extremes: 6, startMs: 1000, stepMs: 70))
            hasFired |= r.Process(sample) is { Kind: GestureKind.DoubleShake };

        // assert
        hasFired.Should().BeTrue("a flat, screen-up phone must stay shakeable despite a stuck 'near'");
    }

    [Fact]
    public void RecognizerSampleGapResetPreservesCoveredClearsLatch()
    {
        var options = new GestureOptions(false, true, false, false, false, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        r.SetProximityCovered(true);
        // act + assert
        r.Process(UpsideDown(0)).Should().BeNull();
        // The >2s gap triggers the recognizer reset: _isUpsideDown clears, the covered level
        // survives. Upright throughout, so the resting-screen-up veto can't confound it.
        foreach (var sample in RestUpright(2600).Concat(BurstUpright(1.4f, extremes: 5, startMs: 3600, stepMs: 70)))
            r.Process(sample).Should().BeNull("covered suppression must survive the gap reset");
        r.SetProximityCovered(false);
        var hasFired = false;
        foreach (var sample in RestUpright(4200).Concat(BurstUpright(1.4f, extremes: 6, startMs: 5200, stepMs: 70)))
            hasFired |= r.Process(sample) is { Kind: GestureKind.DoubleShake };
        hasFired.Should().BeTrue("after uncover the latch must not linger - the gap reset cleared it");
    }

    [Fact]
    public void RecognizerRoutesAPatOnlyWhilePocketed()
    {
        var r = new GestureRecognizer(new GestureOptions(false, false, false, false, true, ShakeSensitivity.Medium));
        var pocketed = PocketRest(0).Concat([Pat(1000)]).Concat(PocketRest(1050, 250)).Concat([Pat(1300)]).Concat(PocketRest(1350));
        // act + assert: upside-down carry latches the pocket guard, so the pat is fed
        pocketed.Select(r.Process).Should().Contain(e => e != null && e.Value.Kind == GestureKind.DoublePat);
        r.Reset();
        var inHand = Enumerable.Range(0, 20).Select(i => Portrait(i * 50))
            .Concat([
                new SensorSample(At(1000), 0f, 1f, 2.5f), Portrait(1050), new SensorSample(At(1300), 0f, 1f, 2.5f),
            ])
            .Concat(Enumerable.Range(0, 20).Select(i => Portrait(1350 + (i * 50))));
        inHand.Select(r.Process).Should().NotContain(e => e != null && e.Value.Kind == GestureKind.DoublePat);
    }

    [Fact]
    public void RecognizerDropsAnAlreadyFaceDownFireWhenOnlyHushIsListening()
    {
        // Mic closed, hush enabled: the phone was lying face down when the wake arrived.
        var r = new GestureRecognizer(new GestureOptions(false, false, false, false, true, ShakeSensitivity.Medium));
        r.SetProximityCovered(true);
        // act + assert
        r.Process(FaceDown(0)).Should().BeNull();
        r.Process(FaceDown(400)).Should().BeNull();
        r.Process(FaceDown(1300)).Should().BeNull("lying there is not a gesture");
    }

    [Fact]
    public void RecognizerFiresAFaceDownEntryWhenOnlyHushIsListening()
    {
        var r = new GestureRecognizer(new GestureOptions(false, false, false, false, true, ShakeSensitivity.Medium));
        r.SetProximityCovered(true);
        // act + assert
        r.Process(FaceUp(0)).Should().BeNull();
        r.Process(FaceDown(100)).Should().BeNull();
        r.Process(FaceDown(900))!.Value.Kind.Should().Be(GestureKind.FaceDown);
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

    [Fact]
    public void ShakeProgressCountsStepsAndClearsOnFire()
    {
        var d = new ShakeDetector(ShakeSensitivity.Medium);
        var samples = Rest(0).Concat(Burst(1.0f, extremes: 3, startMs: 1000, stepMs: 80)).ToList();
        foreach (var s in samples)
            d.Process(s).Should().BeFalse();
        // assert
        var at = samples[^1].At;
        d.GetProgress(at).Should().BeGreaterThan(0.5f).And.BeLessThan(1f);
        d.GetProgress(at + ShakeDetector.ReversalWindow + TimeSpan.FromMilliseconds(1)).Should().Be(0f);
    }

    [Fact]
    public void PatProgressStepsThroughThePair()
    {
        var d = new PatDetector();
        foreach (var s in PocketRest(0))
            d.Process(s);
        // act + assert
        d.Process(Pat(1000));
        d.GetProgress(At(1000)).Should().BeApproximately(1f / 3, 0.01f);
        foreach (var s in PocketRest(1050, 250))
            d.Process(s);
        d.Process(Pat(1300));
        d.GetProgress(At(1300)).Should().BeApproximately(2f / 3, 0.01f);
        PocketRest(1320, 460).Count(d.Process).Should().Be(0, "the pair must first sit out its 500ms hold");
        d.GetProgress(At(1780)).Should().BeGreaterThan(2f / 3).And.BeLessThan(1f);
        d.Process(UpsideDown(1800)).Should().BeTrue();
        d.GetProgress(At(1800)).Should().Be(0f);
    }

    [Fact]
    public void FaceDownProgressFollowsTheDwellAndIsCappedWhileUncovered()
    {
        var d = new FaceDownDetector();
        // act + assert
        d.Process(FaceUp(0));
        d.Process(FaceDown(100));
        d.GetProgress(At(450)).Should().BeApproximately(0.475f, 0.01f);
        d.GetProgress(At(1000)).Should().BeApproximately(0.475f, 0.01f);
        d.SetProximityCovered(true);
        d.Process(FaceDown(600));
        d.GetProgress(At(600)).Should().BeApproximately(0.95f * 500 / 700, 0.01f);
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

    private static IEnumerable<SensorSample> RestUpright(double startMs, double durationMs = 1000, double stepMs = 60)
    {
        for (var t = 0d; t < durationMs; t += stepMs)
            yield return new SensorSample(At(startMs + t), 0f, 1f, 0f);
    }

    private static IEnumerable<SensorSample> BurstUpright(
        float amplitude, int extremes, double startMs, double stepMs)
    {
        // Gravity on Y, so ProximityGuard's resting-screen-up veto stays out of the way and the
        // covered level itself is what the test observes.
        for (var i = 0; i < extremes; i++) {
            var x = i % 2 == 0 ? amplitude : -amplitude;
            yield return new SensorSample(At(startMs + (i * stepMs)), x, 1f, 0f);
        }
    }
}
