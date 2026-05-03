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
}
