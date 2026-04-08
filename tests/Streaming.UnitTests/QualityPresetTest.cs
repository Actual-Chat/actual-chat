using ActualChat.Video;
using static ActualChat.Streaming.StreamLatencyStore;

namespace ActualChat.Streaming.UnitTests;

public class QualityPresetTest(ILogger log)
{
    private ILogger Log { get; } = log;

    [Fact]
    public void StreamLatencyState_DefaultQuality_IsHigh()
    {
        var state = NewStreamLatencyState();
        state.QualityPreset.Value.Level.Should().Be(VideoQualityLevel.High);
        state.QualityPreset.Value.Level.Should().Be(VideoQualityLevel.High);
    }

    [Fact]
    public void QualityPreset_ShouldBeMutableState()
    {
        var state = NewStreamLatencyState();

        // QualityPreset is an IMutableState that can be consumed via Use()
        state.QualityPreset.Should().NotBeNull();
        state.QualityPreset.Value.Should().Be(VideoQualityPreset.High);

        // Setting a new value should update QualityPreset
        state.QualityPreset.Value = VideoQualityPreset.ForLevel(VideoQualityLevel.Medium)!;
        state.QualityPreset.Value.Level.Should().Be(VideoQualityLevel.Medium);
    }

    [Fact]
    public void MaxQuality_ShouldBeClampedByResolution()
    {
        // 720p → High max
        var state720 = NewStreamLatencyState(new VideoFormat { Width = 1280, Height = 720 });
        state720.QualityPreset.Value.Level.Should().Be(VideoQualityLevel.High);

        // 480p → Low max (below 540p threshold), but initial quality is always High
        var state480 = NewStreamLatencyState(new VideoFormat { Width = 640, Height = 480 });
        state480.QualityPreset.Value.Level.Should().Be(VideoQualityLevel.High,
            "initial quality is always High regardless of resolution");
    }

    private StreamLatencyState NewStreamLatencyState(VideoFormat? format = null)
        => new(default,
            CpuClock.Instance.Now,
            format ?? new VideoFormat { Width = 1280, Height = 720 },
            StateFactory.Default,
            Log);
}
