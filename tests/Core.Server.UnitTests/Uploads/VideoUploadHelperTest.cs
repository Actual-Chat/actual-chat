using ActualChat.Uploads;
using FFMpegCore;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public sealed class VideoUploadHelperTest
{
    [Theory]
    [InlineData(0, 1920, 1080, 1920, 1080)]
    [InlineData(180, 1920, 1080, 1920, 1080)]
    [InlineData(90, 1920, 1080, 1080, 1920)]
    [InlineData(270, 1920, 1080, 1080, 1920)]
    [InlineData(-90, 1920, 1080, 1080, 1920)]
    [InlineData(-270, 1920, 1080, 1080, 1920)]
    public void GetEffectiveSizeShouldSwapSidesWhenRotatedByQuarterTurn(
        int rotation,
        int width,
        int height,
        int expectedW,
        int expectedH)
    {
        // arrange
        var video = new VideoStream { Rotation = rotation, Width = width, Height = height };

        // act
        var result = UploadProcessorHelper.GetEffectiveSize(video);

        // assert
        result.Should().Be(new Size2D(expectedW, expectedH));
    }

    [Theory]
    [InlineData("h264", false)]
    [InlineData("hevc", false)]
    [InlineData("h265", false)]
    [InlineData("vp9", true)]
    [InlineData("av1", true)]
    public void MustTranscodeShouldSkipOnlyH264AndHevc(string codecName, bool expected)
    {
        // arrange
        var videoStream = new VideoStream { CodecName = codecName };

        // act
        var result = UploadProcessorHelper.MustTranscode(videoStream);

        // assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(18.3, 18.4, 18.3)]
    [InlineData(0, 4, 4)]
    [InlineData(0, 0, 0)]
    public void AnalyzeVideoShouldFallBackToContainerDuration(
        double streamSeconds,
        double formatSeconds,
        double expectedSeconds)
    {
        // arrange
        var videoStream = new VideoStream {
            Width = 1280,
            Height = 720,
            Duration = TimeSpan.FromSeconds(streamSeconds),
        };
        var format = new FFMpegCore.MediaFormat { Duration = TimeSpan.FromSeconds(formatSeconds) };
        var mediaInfo = new Mock<IMediaAnalysis>(MockBehavior.Strict);
        mediaInfo.SetupGet(x => x.PrimaryVideoStream).Returns(videoStream);
        mediaInfo.SetupGet(x => x.Format).Returns(format);

        // act
        var (_, duration, _) = UploadProcessorHelper.AnalyzeVideo(mediaInfo.Object);

        // assert
        duration.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData("h264", "isom", 1280, 720, VideoConversion.None)]
    [InlineData("hevc", "mp42", 1920, 1080, VideoConversion.None)]
    [InlineData("h264", "iso5", 1280, 720, VideoConversion.Remux)]
    [InlineData("hevc", "qt  ", 1920, 1080, VideoConversion.Remux)]
    [InlineData("av1", "isom", 1280, 720, VideoConversion.Transcode)]
    [InlineData("h264", "isom", 3840, 2160, VideoConversion.Transcode)]
    public void GetConversionShouldRemuxOnlyWhenJustTheContainerIsWrong(
        string codecName,
        string majorBrand,
        int width,
        int height,
        VideoConversion expected)
    {
        // arrange
        var videoStream = new VideoStream { CodecName = codecName, Width = width, Height = height };
        var tags = new Dictionary<string, string> { ["major_brand"] = majorBrand };
        var format = new FFMpegCore.MediaFormat { Tags = tags };
        var mediaInfo = new Mock<IMediaAnalysis>(MockBehavior.Strict);
        mediaInfo.SetupGet(x => x.PrimaryVideoStream).Returns(videoStream);
        mediaInfo.SetupGet(x => x.Format).Returns(format);

        // act
        var result = UploadProcessorHelper.GetConversion(mediaInfo.Object);

        // assert
        result.Should().Be(expected);
    }
}
