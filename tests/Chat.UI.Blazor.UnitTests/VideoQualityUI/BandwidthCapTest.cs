using ActualChat.Bandwidth;
using ActualChat.Rpc;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class BandwidthCapTest
{
    private const long InitialCeiling = 1_000_000;
    private static readonly Moment T0 = new(TimeSpan.Zero);

    private static (BandwidthCap cap, BandwidthEstimator est) NewPair(
        BandwidthCapConfig? capConfig = null,
        BandwidthEstimatorConfig? estConfig = null,
        int cameraCap = 3,
        int screencastCap = 2)
    {
        var layers = new LayerCap(cameraCap, screencastCap);
        var cap = new BandwidthCap(layers, capConfig ?? new BandwidthCapConfig());
        var est = new BandwidthEstimator(estConfig ?? new BandwidthEstimatorConfig(InitialCeiling));
        return (cap, est);
    }

    private static RpcConnectionInfo Conn() => new(1, T0);
    private static Moment At(double sec) => T0 + TimeSpan.FromSeconds(sec);

    [Fact]
    public void FreshCap_StartsAtDeviceMax()
    {
        var (cap, _) = NewPair();
        cap.Layers.CameraLayers.Should().Be(3);
        cap.Layers.ScreencastLayers.Should().Be(2);
    }

    [Fact]
    public void BadStreak_ReducesCameraFirst()
    {
        var (cap, est) = NewPair(new BandwidthCapConfig(BadStreak: 2));
        var conn = Conn();

        est.Tick(conn, At(1), 900_000, 0.3); // bad
        cap.Tick(est);
        cap.Layers.CameraLayers.Should().Be(3, "only one bad tick — below streak");

        est.Tick(conn, At(2), 900_000, 0.3); // bad
        cap.Tick(est);
        cap.Layers.CameraLayers.Should().Be(2, "streak reached → reduce");
    }

    [Fact]
    public void GoodStreak_WithHeadroom_IncreasesScreencastFirst()
    {
        var (cap, est) = NewPair(new BandwidthCapConfig(BadStreak: 1, GoodStreak: 2));
        var conn = Conn();

        // Reduce camera and screencast to 1
        est.Tick(conn, At(1), 900_000, 0.3); cap.Tick(est);
        est.Tick(conn, At(2), 900_000, 0.3); cap.Tick(est);
        est.Tick(conn, At(3), 900_000, 0.3); cap.Tick(est);
        cap.Layers.CameraLayers.Should().Be(1);
        cap.Layers.ScreencastLayers.Should().Be(1);

        // Wait past cooldown then push good ticks at-or-above ConfirmRatio
        var calmStart = 100.0;
        for (var i = 0; i < 2; i++) {
            est.Tick(conn, At(calmStart + i), (long)(est.CeilingBps * 0.95), 1.0);
            cap.Tick(est);
        }

        cap.Layers.ScreencastLayers.Should().Be(2, "screencast lifts first");
    }

    [Fact]
    public void Reduce_OnlyConsumesEachIncomingStreakOnce()
    {
        var (cap, est) = NewPair(new BandwidthCapConfig(BadStreak: 2));
        var conn = Conn();

        est.Tick(conn, At(1), 900_000, 0.3);
        est.Tick(conn, At(2), 900_000, 0.3);
        cap.Tick(est); // streak=2, reduces 3→2
        cap.Tick(est); // same streak — no further reduce
        cap.Tick(est);
        cap.Layers.CameraLayers.Should().Be(2);

        est.Tick(conn, At(3), 900_000, 0.3); // streak=3
        cap.Tick(est);
        cap.Layers.CameraLayers.Should().Be(1, "new bad tick beyond consumed streak → reduce again");
    }

    [Fact]
    public void GoodStreak_WithoutHeadroom_DoesNotLift()
    {
        var (cap, est) = NewPair(new BandwidthCapConfig(BadStreak: 1, GoodStreak: 2));
        var conn = Conn();

        // Drive cap down once
        est.Tick(conn, At(1), 900_000, 0.3); cap.Tick(est);
        cap.Layers.CameraLayers.Should().Be(2);

        // Good ticks but far below ceiling — no headroom proof
        for (var i = 0; i < 5; i++) {
            est.Tick(conn, At(100 + i), 1_000, 1.0); // tiny throughput
            cap.Tick(est);
        }

        cap.Layers.CameraLayers.Should().Be(2, "no headroom proof — no lift");
    }
}
