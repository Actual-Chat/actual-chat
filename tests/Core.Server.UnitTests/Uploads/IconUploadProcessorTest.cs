using ActualChat.Uploads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class IconUploadProcessorTest : IDisposable
{
    private readonly IconUploadProcessor _processor;
    private readonly List<ProcessedFile> _processedFiles = new();

    public IconUploadProcessorTest()
    {
        var logger = new Mock<ILogger<IconUploadProcessor>>(MockBehavior.Loose);
        _processor = new IconUploadProcessor(logger.Object);
    }

    public void Dispose()
    {
        foreach (var pf in _processedFiles)
            pf.Dispose();
    }

    // Supports tests

    [Theory]
    [InlineData("image/jpeg", MediaKind.ChatPicture, true)]
    [InlineData("image/png", MediaKind.ChatPicture, true)]
    [InlineData("image/webp", MediaKind.ChatPicture, true)]
    [InlineData("image/avif", MediaKind.ChatPicture, true)]
    [InlineData("image/heif", MediaKind.ChatPicture, true)]
    [InlineData("image/heic", MediaKind.ChatPicture, true)]
    [InlineData("image/bmp", MediaKind.ChatPicture, true)]
    [InlineData("image/svg+xml", MediaKind.ChatPicture, true)]
    [InlineData("image/jpeg", MediaKind.UserPicture, true)]
    [InlineData("image/webp", MediaKind.UserAvatarPicture, true)]
    [InlineData("image/gif", MediaKind.ChatPicture, false)] // GIF excluded
    [InlineData("image/jpeg", MediaKind.LinkPreviewPicture, false)] // not a chat icon
    [InlineData("image/jpeg", MediaKind.ChatEntryAttachment, false)] // not a chat icon
    [InlineData("image/png", MediaKind.Unknown, false)]
    [InlineData("video/mp4", MediaKind.ChatPicture, false)]
    [InlineData("text/plain", MediaKind.ChatPicture, false)]
    public void Supports_ReturnsExpectedResult(string contentType, MediaKind mediaKind, bool expected)
        => _processor.Supports(contentType, mediaKind).Should().Be(expected);

    // SVG → PNG conversion

    [Fact]
    public async Task Process_Svg_ConvertsToPng()
    {
        var svgContent = """
            <?xml version="1.0" encoding="UTF-8"?>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
              <circle cx="50" cy="50" r="40" fill="blue"/>
            </svg>
            """u8.ToArray();
        var upload = CreateUploadedFile("test.svg", "image/svg+xml", svgContent);

        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        result.File.ContentType.Should().Be("image/png");
        result.File.FileName.ToString().Should().EndWith(".png");
        await AssertIsPng(result.File);
    }

    // JPEG passthrough (universal format → ProcessRaster path)

    [Fact]
    public async Task Process_Jpeg_KeepsFormat()
    {
        var imageBytes = CreateTestJpeg(200, 200);
        var upload = CreateUploadedFile("photo.jpg", "image/jpeg", imageBytes);

        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        result.File.ContentType.Should().Be("image/jpeg");
        result.Size.Should().NotBeNull();
        await AssertIsJpeg(result.File);
    }

    // PNG passthrough (universal format → ProcessRaster path)

    [Fact]
    public async Task Process_Png_KeepsFormat()
    {
        var imageBytes = CreateTestPng(150, 150);
        var upload = CreateUploadedFile("icon.png", "image/png", imageBytes);

        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        result.File.ContentType.Should().Be("image/png");
        result.Size.Should().NotBeNull();
        await AssertIsPng(result.File);
    }

    // WebP → PNG conversion (non-universal format)

    [Fact]
    public async Task Process_WebP_ConvertsToPng()
    {
        var imageBytes = CreateTestWebp(100, 100);
        var upload = CreateUploadedFile("icon.webp", "image/webp", imageBytes);

        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        result.File.ContentType.Should().Be("image/png");
        result.File.FileName.ToString().Should().EndWith(".png");
        result.Size.Should().NotBeNull();
        await AssertIsPng(result.File);
    }

    // BMP → PNG conversion (non-universal format)

    [Fact]
    public async Task Process_Bmp_ConvertsToPng()
    {
        var imageBytes = CreateTestBmp(80, 80);
        var upload = CreateUploadedFile("icon.bmp", "image/bmp", imageBytes);

        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        result.File.ContentType.Should().Be("image/png");
        result.File.FileName.ToString().Should().EndWith(".png");
        result.Size.Should().NotBeNull();
        await AssertIsPng(result.File);
    }

    // Resize test

    [Fact]
    public async Task Process_OversizedImage_ResizesWithinLimit()
    {
        var imageBytes = CreateTestPng(3000, 2000);
        var upload = CreateUploadedFile("huge.png", "image/png", imageBytes);

        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        result.Size.Should().NotBeNull();
        result.Size!.Value.Width.Should().BeLessThanOrEqualTo(1920);
        result.Size!.Value.Height.Should().BeLessThanOrEqualTo(1920);
    }

    // Helpers

    private static byte[] CreateTestPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static byte[] CreateTestJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    private static byte[] CreateTestWebp(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new WebpEncoder());
        return ms.ToArray();
    }

    private static byte[] CreateTestBmp(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new BmpEncoder());
        return ms.ToArray();
    }

    private static UploadedStreamFile CreateUploadedFile(string fileName, string contentType, byte[] data)
        => new(fileName, contentType, data.Length, () => Task.FromResult<Stream>(new MemoryStream(data)));

    private static async Task AssertIsPng(UploadedFile file)
    {
        var stream = await file.Open();
        await using var _ = stream;
        var info = await Image.IdentifyAsync(stream);
        info.Should().NotBeNull();
        info!.Metadata.DecodedImageFormat!.DefaultMimeType.Should().Be("image/png");
    }

    private static async Task AssertIsJpeg(UploadedFile file)
    {
        var stream = await file.Open();
        await using var _ = stream;
        var info = await Image.IdentifyAsync(stream);
        info.Should().NotBeNull();
        info!.Metadata.DecodedImageFormat!.DefaultMimeType.Should().Be("image/jpeg");
    }
}
