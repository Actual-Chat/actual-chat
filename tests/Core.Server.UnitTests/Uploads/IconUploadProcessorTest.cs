using ActualChat.Uploads;
using ActualLab.IO;
using SixLabors.ImageSharp;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class IconUploadProcessorTest : IDisposable
{
    private readonly IconUploadProcessor _processor;
    private readonly List<ProcessedFile> _processedFiles = new();

    public IconUploadProcessorTest()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<RasterImageNormalizer>()
            .AddSingleton<SvgRasterizer>()
            .BuildServiceProvider();
        _processor = new IconUploadProcessor(services);
    }

    public void Dispose()
    {
        foreach (var pf in _processedFiles)
            pf.DisposeSilently();
    }

    [Theory]
    [InlineData("image/jpeg", MediaKind.ChatPicture, true)]
    [InlineData("image/png", MediaKind.ChatPicture, true)]
    [InlineData("image/webp", MediaKind.ChatPicture, true)]
    [InlineData("image/bmp", MediaKind.ChatPicture, true)]
    [InlineData("image/svg+xml", MediaKind.ChatPicture, true)]
    [InlineData("image/jpeg", MediaKind.UserPicture, true)]
    [InlineData("image/webp", MediaKind.UserAvatarPicture, true)]
    [InlineData("image/avif", MediaKind.ChatPicture, false)] // not in SupportedAvatarContentTypes
    [InlineData("image/heif", MediaKind.ChatPicture, false)] // not in SupportedAvatarContentTypes
    [InlineData("image/heic", MediaKind.ChatPicture, false)] // not in SupportedAvatarContentTypes
    [InlineData("image/gif", MediaKind.ChatPicture, false)] // GIF excluded
    [InlineData("image/jpeg", MediaKind.LinkPreviewPicture, false)] // not a chat icon
    [InlineData("image/jpeg", MediaKind.ChatEntryAttachment, false)] // not a chat icon
    [InlineData("image/png", MediaKind.Unknown, false)]
    [InlineData("video/mp4", MediaKind.ChatPicture, false)]
    [InlineData("text/plain", MediaKind.ChatPicture, false)]
    public void ShouldSupportExpectedContentTypes(string contentType, MediaKind mediaKind, bool expected)
        => _processor.Supports(contentType, mediaKind).Should().Be(expected);

    [Fact]
    public async Task ShouldConvertSvgToPng()
    {
        // arrange
        var svgContent = """
            <?xml version="1.0" encoding="UTF-8"?>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
              <circle cx="50" cy="50" r="40" fill="blue"/>
            </svg>
            """u8.ToArray();
        var upload = TestImages.CreateUploadedFile("test.svg", "image/svg+xml", svgContent);

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.File.ContentType.Should().Be("image/png");
        result.File.FileName.ToString().Should().EndWith(".png");
        await AssertImageFormat(result.File, "image/png");
    }

    [Fact]
    public async Task ShouldKeepJpegFormat()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpeg(200, 200));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.File.ContentType.Should().Be("image/jpeg");
        result.Size.Should().NotBeNull();
        await AssertImageFormat(result.File, "image/jpeg");
    }

    [Fact]
    public async Task ShouldKeepPngFormat()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("icon.png", "image/png", TestImages.CreatePng(150, 150));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.File.ContentType.Should().Be("image/png");
        result.Size.Should().NotBeNull();
        await AssertImageFormat(result.File, "image/png");
    }

    [Fact]
    public async Task ShouldKeepWebpFormat()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("icon.webp", "image/webp", TestImages.CreateWebp(100, 100));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.File.ContentType.Should().Be("image/webp");
        result.Size.Should().NotBeNull();
        await AssertImageFormat(result.File, "image/webp");
    }

    [Fact]
    public async Task ShouldConvertBmpToPng()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("icon.bmp", "image/bmp", TestImages.CreateBmp(80, 80));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.File.ContentType.Should().Be("image/png");
        result.File.FileName.ToString().Should().EndWith(".png");
        result.Size.Should().NotBeNull();
        await AssertImageFormat(result.File, "image/png");
    }

    [Fact]
    public async Task ShouldResizeOversizedImage()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("huge.png", "image/png", TestImages.CreatePng(3000, 2000));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        result.Size.Should().NotBeNull();
        result.Size!.Value.Width.Should().BeLessThanOrEqualTo(Constants.Attachments.MaxIconSize);
        result.Size!.Value.Height.Should().BeLessThanOrEqualTo(Constants.Attachments.MaxIconSize);
    }

    [Fact]
    public async Task KeepsPassthroughTempFileInsideTempDirectory()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile(
            "../outside/icon.png",
            "image/png",
            TestImages.CreatePng(100, 100));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        var tempFile = AssertTempFileInsideDirectory(result);
        tempFile.FileName.Value.Should().Be("icon.png");
    }

    [Fact]
    public async Task KeepsNormalizedTempFileInsideTempDirectory()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile(
            "../outside/icon.bmp",
            "image/bmp",
            TestImages.CreateBmp(80, 80));

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        var tempFile = AssertTempFileInsideDirectory(result);
        tempFile.FileName.Value.Should().Be("icon.png");
    }

    [Fact]
    public async Task KeepsConvertedTempFileInsideTempDirectory()
    {
        // arrange
        var svgContent = """
            <?xml version="1.0" encoding="UTF-8"?>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
              <circle cx="50" cy="50" r="40" fill="blue"/>
            </svg>
            """u8.ToArray();
        var upload = TestImages.CreateUploadedFile("../outside/icon.svg", "image/svg+xml", svgContent);

        // act
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);

        // assert
        var tempFile = AssertTempFileInsideDirectory(result);
        tempFile.FileName.Value.Should().StartWith("icon-").And.EndWith(".png");
    }

    // Private methods

    private static UploadedTempFile AssertTempFileInsideDirectory(ProcessedFile result)
    {
        var tempFile = result.File.Should().BeOfType<UploadedTempFile>().Subject;
        var tempDirectory = FilePath.GetApplicationTempDirectory().FullPath;
        tempFile.TempFilePath.FullPath.IsSubPathOf(tempDirectory).Should().BeTrue();
        tempFile.TempFilePath.FileName.Should().NotBe(tempFile.FileName);
        return tempFile;
    }

    private static async Task AssertImageFormat(UploadedFile file, string expectedMimeType)
    {
        var stream = await file.Open();
        await using var _ = stream;
        var info = await Image.IdentifyAsync(stream);
        info.Should().NotBeNull();
        info!.Metadata.DecodedImageFormat!.DefaultMimeType.Should().Be(expectedMimeType);
    }
}
