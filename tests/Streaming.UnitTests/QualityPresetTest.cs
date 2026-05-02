using ActualChat.Video;

namespace ActualChat.Streaming.UnitTests;

public class QualityPresetTest
{
    // --- VideoQualityPreset.ForLevel ---------------------------------------

    [Theory]
    [InlineData(VideoQualityLevel.Ultra, 3840, 2160)]
    [InlineData(VideoQualityLevel.Full, 1920, 1080)]
    [InlineData(VideoQualityLevel.High, 1280, 720)]
    [InlineData(VideoQualityLevel.Medium, 960, 540)]
    [InlineData(VideoQualityLevel.Low, 640, 360)]
    public void ForLevel_ReturnsExpectedDimensions(VideoQualityLevel level, int expectedW, int expectedH)
    {
        var preset = VideoQualityPreset.ForLevel(level);
        preset.Width.Should().Be(expectedW);
        preset.Height.Should().Be(expectedH);
        preset.Level.Should().Be(level);
    }

    // --- StepUp / StepDown (kind-agnostic) ---------------------------------

    [Theory]
    [InlineData(VideoQualityLevel.Low, VideoQualityLevel.Medium)]
    [InlineData(VideoQualityLevel.Medium, VideoQualityLevel.High)]
    [InlineData(VideoQualityLevel.High, VideoQualityLevel.Full)]
    [InlineData(VideoQualityLevel.Full, VideoQualityLevel.Ultra)]
    public void StepUp_AdvancesOneTier(VideoQualityLevel from, VideoQualityLevel expected)
    {
        var stepped = VideoQualityPreset.StepUp(from);
        stepped.Should().NotBeNull();
        stepped!.Level.Should().Be(expected);
    }

    [Fact]
    public void StepUp_FromUltra_ReturnsNull()
        => VideoQualityPreset.StepUp(VideoQualityLevel.Ultra).Should().BeNull();

    [Theory]
    [InlineData(VideoQualityLevel.Ultra, VideoQualityLevel.Full)]
    [InlineData(VideoQualityLevel.Full, VideoQualityLevel.High)]
    [InlineData(VideoQualityLevel.High, VideoQualityLevel.Medium)]
    [InlineData(VideoQualityLevel.Medium, VideoQualityLevel.Low)]
    public void StepDown_DropsOneTier(VideoQualityLevel from, VideoQualityLevel expected)
    {
        var stepped = VideoQualityPreset.StepDown(from);
        stepped.Should().NotBeNull();
        stepped!.Level.Should().Be(expected);
    }

    [Fact]
    public void StepDown_FromLow_ReturnsNull()
        => VideoQualityPreset.StepDown(VideoQualityLevel.Low).Should().BeNull();

    // --- Kind-aware StepDown ------------------------------------------------

    [Fact]
    public void StepDown_Screencast_FloorsAtMedium()
    {
        // Screencast floors at Medium (540p) — below that, IDE text is unreadable
        // regardless of bitrate, so pausing is preferable to sending garbage.
        VideoQualityPreset.StepDown(VideoQualityLevel.Medium, StreamKind.Screencast).Should().BeNull();
    }

    [Fact]
    public void StepDown_Webcam_AllowsMediumToLow()
        => VideoQualityPreset.StepDown(VideoQualityLevel.Medium, StreamKind.Webcam)!
            .Level.Should().Be(VideoQualityLevel.Low);

    // --- Numeric ordering preserved (lower value = higher quality) ---------

    [Fact]
    public void EnumOrder_LowerValueIsHigherQuality()
    {
        ((int)VideoQualityLevel.Ultra).Should().BeLessThan((int)VideoQualityLevel.Full);
        ((int)VideoQualityLevel.Full).Should().BeLessThan((int)VideoQualityLevel.High);
        ((int)VideoQualityLevel.High).Should().BeLessThan((int)VideoQualityLevel.Medium);
        ((int)VideoQualityLevel.Medium).Should().BeLessThan((int)VideoQualityLevel.Low);
    }
}
