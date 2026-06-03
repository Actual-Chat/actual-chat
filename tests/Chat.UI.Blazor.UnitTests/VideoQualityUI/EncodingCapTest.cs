using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class EncodingCapTest
{
    private static EncodingCap NewCap(EncodingCapConfig? config = null, int cameraCap = 3, int screencastCap = 2)
        => new(new LayerCap(cameraCap, screencastCap), config ?? new EncodingCapConfig());

    [Fact]
    public void FreshCap_StartsAtDeviceMax()
    {
        var cap = NewCap();
        cap.Layers.CameraLayers.Should().Be(3);
        cap.Layers.ScreencastLayers.Should().Be(2);
    }

    [Fact]
    public void SustainedBad_ReducesCameraFirst()
    {
        var cap = NewCap(new EncodingCapConfig(BadStreak: 2));

        cap.Tick(0.5); // bad #1, streak builds
        cap.Tick(0.5); // bad #2 → reduce

        cap.Layers.CameraLayers.Should().Be(2);
        cap.Layers.ScreencastLayers.Should().Be(2);
    }

    [Fact]
    public void SustainedBad_OnlyTouchesScreencastAfterCameraIsAtFloor()
    {
        var cap = NewCap(new EncodingCapConfig(BadStreak: 1));

        cap.Tick(0.5); // reduce camera 3→2
        cap.Tick(0.5); // reduce camera 2→1
        cap.Tick(0.5); // reduce screencast 2→1
        cap.Tick(0.5); // both at floor — no change

        cap.Layers.CameraLayers.Should().Be(1);
        cap.Layers.ScreencastLayers.Should().Be(1);
    }

    [Fact]
    public void SustainedGood_IncreasesScreencastFirst()
    {
        var cap = NewCap(new EncodingCapConfig(BadStreak: 1, GoodStreak: 1));

        cap.Tick(0.5); cap.Tick(0.5); cap.Tick(0.5); // drive both down
        cap.Layers.ScreencastLayers.Should().Be(1);
        cap.Layers.CameraLayers.Should().Be(1);

        cap.Tick(0.0); // good — increase screencast first
        cap.Layers.ScreencastLayers.Should().Be(2);
        cap.Layers.CameraLayers.Should().Be(1);

        cap.Tick(0.0); // screencast at cap → camera
        cap.Layers.CameraLayers.Should().Be(2);
    }

    [Fact]
    public void NeutralRatio_DoesNotChangeLayers()
    {
        var cap = NewCap();
        for (var i = 0; i < 10; i++)
            cap.Tick(0.10); // between Good (0.05) and Bad (0.20)

        cap.Layers.CameraLayers.Should().Be(3);
        cap.Layers.ScreencastLayers.Should().Be(2);
    }

    [Fact]
    public void DeadBandDecaysGoodStreakInsteadOfResetting()
    {
        var cap = NewCap(new EncodingCapConfig(BadStreak: 1), cameraCap: 3);
        cap.Tick(0.5); cap.Tick(0.5); // drive camera 3→1
        cap.Layers.CameraLayers.Should().Be(1);

        // Mostly-good with an occasional dead-band blip must still reach the
        // 5-tick good streak and ramp — a hard reset would never get there.
        cap.Tick(0.0); // g1
        cap.Tick(0.0); // g2
        cap.Tick(0.0); // g3
        cap.Tick(0.10); // dead band → decay to 2 (not 0)
        cap.Tick(0.0); // g3
        cap.Tick(0.0); // g4
        cap.Tick(0.0); // g5 → increase
        cap.Layers.CameraLayers.Should().Be(2);
    }

    [Fact]
    public void SustainedDeadBandDoesNotRamp()
    {
        var cap = NewCap(new EncodingCapConfig(BadStreak: 1), cameraCap: 3);
        cap.Tick(0.5); cap.Tick(0.5); // drive camera 3→1
        cap.Layers.CameraLayers.Should().Be(1);

        for (var i = 0; i < 20; i++)
            cap.Tick(0.10); // steady marginal underrun — must NOT ramp

        cap.Layers.CameraLayers.Should().Be(1);
    }

    [Fact]
    public void StreakResetsOnOppositeSignal()
    {
        var cap = NewCap(new EncodingCapConfig(BadStreak: 3));

        cap.Tick(0.5); // bad 1
        cap.Tick(0.5); // bad 2
        cap.Tick(0.0); // good — resets bad streak
        cap.Tick(0.5); // bad 1 again

        cap.Layers.CameraLayers.Should().Be(3, "bad streak didn't reach BadStreak=3 again yet");
    }
}
