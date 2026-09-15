using ActualChat.Media;

namespace ActualChat.Core.UnitTests.Media;

public class MediaTypeExtTest
{
    [Theory]
    [InlineData("application/octet-stream", "IMG_4351.HEIC", "image/heic")]
    [InlineData("", "photo.heic", "image/heic")]
    [InlineData(null, "clip.mp4", "video/mp4")]
    [InlineData("application/octet-stream", "shot.avif", "image/avif")]
    public void ShouldInferTheContentTypeBrowsersOmit(string? contentType, string fileName, string expected)
        => MediaTypeExt.NormalizeContentType(contentType, fileName).Should().Be(expected);

    [Theory]
    [InlineData("image/jpeg", "photo.heic")]
    [InlineData("image/png", "mislabeled.jpg")]
    public void ShouldKeepAContentTypeTheBrowserActuallySent(string contentType, string fileName)
        => MediaTypeExt.NormalizeContentType(contentType, fileName).Should().Be(contentType);

    [Theory]
    [InlineData("application/octet-stream", "notes.txt")]
    [InlineData("application/octet-stream", "noextension")]
    public void ShouldLeaveUnknownExtensionsAlone(string contentType, string fileName)
        => MediaTypeExt.NormalizeContentType(contentType, fileName).Should().Be(contentType);

    [Fact]
    public void NormalizedHeicShouldReachTheImagePipeline()
    {
        // arrange - the whole point: IsSupportedImage is the gate the raw type failed
        MediaTypeExt.IsSupportedImage("application/octet-stream").Should().BeFalse();

        // act
        var normalized = MediaTypeExt.NormalizeContentType("application/octet-stream", "IMG_4351.HEIC");

        // assert
        MediaTypeExt.IsSupportedImage(normalized).Should().BeTrue();
    }
}
