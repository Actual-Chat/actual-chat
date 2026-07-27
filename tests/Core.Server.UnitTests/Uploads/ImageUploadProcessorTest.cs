using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class ImageUploadProcessorTest : IDisposable
{
    private readonly ImageUploadProcessor _processor;
    private readonly List<ProcessedFile> _processedFiles = new();

    public ImageUploadProcessorTest()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<RasterImageNormalizer>()
            .BuildServiceProvider();
        _processor = new ImageUploadProcessor(services);
    }

    public void Dispose()
    {
        foreach (var pf in _processedFiles)
            pf.DisposeSilently();
    }

    [Theory]
    [InlineData("image/jpeg", MediaKind.ChatEntryAttachment, true)]
    [InlineData("image/png", MediaKind.LinkPreviewPicture, true)]
    [InlineData("image/webp", MediaKind.LinkPreviewPicture, true)]
    [InlineData("image/avif", MediaKind.ChatEntryAttachment, true)]
    [InlineData("image/jpeg", MediaKind.Unknown, true)]
    [InlineData("image/jpeg", MediaKind.ChatPicture, false)] // icon → IconUploadProcessor
    [InlineData("image/png", MediaKind.UserPicture, false)] // icon → IconUploadProcessor
    [InlineData("image/webp", MediaKind.UserAvatarPicture, false)] // icon → IconUploadProcessor
    [InlineData("image/gif", MediaKind.ChatEntryAttachment, false)] // GIF excluded
    [InlineData("image/svg+xml", MediaKind.LinkPreviewPicture, false)] // SVG excluded
    [InlineData("video/mp4", MediaKind.ChatEntryAttachment, false)]
    [InlineData("text/plain", MediaKind.Unknown, false)]
    public void ShouldSupportExpectedContentTypes(string contentType, MediaKind mediaKind, bool expected)
        => _processor.Supports(contentType, mediaKind).Should().Be(expected);

    [Fact]
    public async Task ShouldPreserveWebpFormat()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("preview.webp", "image/webp", TestImages.CreateWebp(100, 100));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.File.ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task ShouldPreserveJpegFormat()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpeg(200, 200));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.File.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task RejectsImageExceedingPixelBudget()
    {
        // arrange
        var data = TestImages.CreatePngHeader(65535, 65535);
        var upload = TestImages.CreateUploadedFile("huge.png", "image/png", data);

        // act
        var process = () => _processor.Process(upload, null, CancellationToken.None);

        // assert
        await process.Should().ThrowAsync<InvalidOperationException>().WithMessage("*too big*");
    }

    [Fact]
    public async Task RejectsExcessiveFrameCount()
    {
        // arrange
        var data = TestImages.CreateAnimatedWebp(1, 1, ImageLimits.MaxFrameCount + 1);
        var upload = TestImages.CreateUploadedFile("animation.webp", "image/webp", data);

        // act
        var process = () => _processor.Process(upload, null, CancellationToken.None);

        // assert
        await process.Should().ThrowAsync<InvalidOperationException>().WithMessage("*too many frames*");
    }

    [Fact]
    public async Task AcceptsPhotoSizedImage()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpeg(4000, 3000));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.File.ContentType.Should().Be("image/jpeg");
        result.Size.Should().Be(new Size2D(1920, 1440));
    }
}
