using ActualChat.Uploads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class ImageUploadProcessorTest : IDisposable
{
    private readonly ImageUploadProcessor _processor;
    private readonly List<ProcessedFile> _processedFiles = new();

    public ImageUploadProcessorTest()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        _processor = new ImageUploadProcessor(services);
    }

    public void Dispose()
    {
        foreach (var pf in _processedFiles)
            pf.Dispose();
    }

    // Supports tests — icon media kinds must be rejected

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
    public void Supports_ReturnsExpectedResult(string contentType, MediaKind mediaKind, bool expected)
        => _processor.Supports(contentType, mediaKind).Should().Be(expected);

    // Processing tests — preserves original format

    [Fact]
    public async Task Process_WebP_PreservesFormat()
    {
        var imageBytes = CreateTestWebp(100, 100);
        var upload = CreateUploadedFile("preview.webp", "image/webp", imageBytes);

        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        result.File.ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task Process_Jpeg_PreservesFormat()
    {
        var imageBytes = CreateTestJpeg(200, 200);
        var upload = CreateUploadedFile("photo.jpg", "image/jpeg", imageBytes);

        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        result.File.ContentType.Should().Be("image/jpeg");
    }

    // Helpers

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

    private static UploadedStreamFile CreateUploadedFile(string fileName, string contentType, byte[] data)
        => new(fileName, contentType, data.Length, () => Task.FromResult<Stream>(new MemoryStream(data)));
}
