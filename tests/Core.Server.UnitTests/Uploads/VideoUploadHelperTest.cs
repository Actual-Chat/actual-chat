using ActualChat.Uploads;
using FFMpegCore;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class VideoUploadHelperTest
{
    [Theory]
    [InlineData(0, 1920, 1080, 1920, 1080)]
    [InlineData(180, 1920, 1080, 1920, 1080)]
    [InlineData(90, 1920, 1080, 1080, 1920)]
    [InlineData(270, 1920, 1080, 1080, 1920)]
    [InlineData(-90, 1920, 1080, 1080, 1920)]
    [InlineData(-270, 1920, 1080, 1080, 1920)]
    public void GetEffectiveSize_ReturnsCorrectSize(int rotation, int width, int height, int expectedW, int expectedH)
    {
        var video = new VideoStream { Rotation = rotation, Width = width, Height = height };
        var result = UploadProcessorHelper.GetEffectiveSize(video);
        result.Should().Be(new Size2D(expectedW, expectedH));
    }

    [Theory]
    [InlineData("h264", false)]
    [InlineData("hevc", false)]
    [InlineData("h265", false)]
    [InlineData("vp9", true)]
    [InlineData("av1", true)]
    public void MustConvert_ReturnsExpectedResult(string codecName, bool expected)
    {
        var videoStream = new VideoStream { CodecName = codecName };
        var result = UploadProcessorHelper.MustConvertVideo(videoStream);
        result.Should().Be(expected);
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
