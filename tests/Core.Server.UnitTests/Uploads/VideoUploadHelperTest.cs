using ActualChat.Uploads;
using FFMpegCore;
using SixLabors.ImageSharp;

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
        var result = UploadHelper.GetEffectiveSize(video);
        result.Should().Be(new Size(expectedW, expectedH));
    }

    [Theory]
    [InlineData(".webm", "h264", true)]
    [InlineData(".mkv", "h264", true)]
    [InlineData(".avi", "hevc", true)]
    [InlineData(".mp4", "h264", false)]
    [InlineData(".mp4", "hevc", false)]
    [InlineData(".mp4", "h265", false)]
    [InlineData(".mp4", "vp9", true)]
    [InlineData(".mp4", "av1", true)]
    [InlineData(".MP4", "h264", false)]
    [InlineData(".Mp4", "hevc", false)]
    public void MustConvert_ReturnsExpectedResult(string extension, string codecName, bool expected)
    {
        var videoStream = new VideoStream { CodecName = codecName };
        var media = new Mock<IMediaAnalysis>();
        media.Setup(m => m.PrimaryVideoStream).Returns(videoStream);

        var result = UploadHelper.MustConvertVideo(media.Object, "video" + extension);
        result.Should().Be(expected);
    }

    [Fact]
    public void MustConvert_NullPrimaryVideoStream_ReturnsTrue()
    {
        var media = new Mock<IMediaAnalysis>();
        media.Setup(m => m.PrimaryVideoStream).Returns((VideoStream?)null);

        var result = UploadHelper.MustConvertVideo(media.Object, "video.mp4");
        result.Should().BeTrue();
    }
}
