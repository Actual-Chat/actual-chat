using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class AttachmentImageUploadProcessorTest : IDisposable
{
    private readonly AttachmentImageUploadProcessor _processor
        = new(new ServiceCollection().AddLogging().BuildServiceProvider());
    private readonly List<ProcessedFile> _processedFiles = new();

    public void Dispose()
    {
        foreach (var pf in _processedFiles)
            pf.DisposeSilently();
    }

    [Theory]
    [InlineData("image/jpeg", MediaKind.ChatEntryAttachment, true)]
    [InlineData("image/heic", MediaKind.ChatEntryAttachment, true)]
    [InlineData("image/gif", MediaKind.ChatEntryAttachment, true)]
    [InlineData("image/gif", MediaKind.UserPicture, false)]
    [InlineData("image/svg+xml", MediaKind.ChatEntryAttachment, false)]
    [InlineData("image/jpeg", MediaKind.LinkPreviewPicture, false)]
    [InlineData("image/png", MediaKind.UserPicture, false)]
    [InlineData("video/mp4", MediaKind.ChatEntryAttachment, false)]
    public void ShouldSupportOnlyChatAttachmentImages(string contentType, MediaKind mediaKind, bool expected)
        => _processor.Supports(contentType, mediaKind).Should().Be(expected);

    [Fact]
    public async Task ShouldSizeAGifWithoutRewritingIt()
    {
        // arrange - without a size the message list has no aspect ratio and lays the tile out square
        var data = TestImages.CreateAnimatedGif(972, 730, 3);
        var upload = TestImages.CreateUploadedFile("motion.gif", "image/gif", data);

        // act
        var result = await Process(upload);
        var stored = await ReadAll(result.File);

        // assert
        result.Size.Should().Be(new Size2D(972, 730));
        stored.Should().Equal(data, "a GIF must reach storage byte for byte - stripping would re-encode it");
        result.File.ContentType.Should().Be("image/gif");
    }

    [Fact]
    public async Task ShouldStripMetadataWithoutReencoding()
    {
        // arrange
        var data = TestImages.CreateJpegWithExif(400, 300, 1);
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", data);

        // act
        var result = await Process(upload);
        var stored = await ReadAll(result.File);

        // assert
        stored.Should().Equal(ImageMetadataStripper.Strip(data));
        stored.Length.Should().BeLessThan(data.Length, "the EXIF segment must be gone");
        result.File.ContentType.Should().Be("image/jpeg");
        result.Size.Should().Be(new Size2D(400, 300));
    }

    [Fact]
    public async Task ShouldKeepFileUntouchedWhenKeepMetadataIsSet()
    {
        // arrange
        var data = TestImages.CreateJpegWithExif(400, 300, 1);
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", data) with { KeepMetadata = true };

        // act
        var result = await Process(upload);

        // assert
        result.File.Should().BeSameAs(upload);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldStoreHeicAsImageSizedFromItsBoxes(bool keepMetadata)
    {
        // arrange - ImageSharp can't open HEIC, which used to turn every original HEIC into a file
        var data = TestImages.CreateHeif(4032, 3024, 270, HeifReaderTest.Exif("GPSSECRET"));
        var upload = TestImages.CreateUploadedFile("photo.heic", "image/heic", data) with {
            KeepMetadata = keepMetadata,
        };

        // act
        var result = await Process(upload);
        var stored = await ReadAll(result.File);

        // assert
        result.File.ContentType.Should().Be("image/heic");
        result.Size.Should().Be(new Size2D(3024, 4032));
        stored.Should().Equal(keepMetadata ? data : ImageMetadataStripper.Strip(data));
    }

    [Fact]
    public async Task ShouldReportDisplayDimensionsForRotatedPhotos()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpegWithExif(400, 300, 6));

        // act
        var result = await Process(upload);

        // assert
        result.Size.Should().Be(new Size2D(300, 400));
    }

    [Fact]
    public async Task ShouldNotResizeLargePhotos()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpeg(4000, 3000));

        // act
        var result = await Process(upload);

        // assert
        result.Size.Should().Be(new Size2D(4000, 3000));
    }

    [Fact]
    public async Task ShouldStoreAnOversizedImageAsABinaryFile()
    {
        // arrange: a header that claims more pixels than the server stores, without any pixel data
        var upload = TestImages.CreateUploadedFile("huge.png", "image/png", TestImages.CreatePngHeader(13000, 9000));

        // act
        var result = await Process(upload);

        // assert: no exception, and the result is stored as a binary file
        result.Size.Should().BeNull();
        result.File.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task ShouldStoreImageExceedingPixelBudgetAsBinaryFile()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("huge.png", "image/png", TestImages.CreatePngHeader(65535, 65535));

        // act
        var result = await Process(upload);

        // assert
        result.Size.Should().BeNull();
        result.File.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task ShouldStripMetadataFromAnOversizedImage()
    {
        // arrange: oversized on one side only, so the file stays small enough to carry real EXIF
        var data = TestImages.CreateJpegWithExif(13000, 1, 1);
        var upload = TestImages.CreateUploadedFile("huge.jpg", "image/jpeg", data);

        // act
        var result = await Process(upload);
        var stored = await ReadAll(result.File);

        // assert: still stored as a binary file, but with the EXIF segment gone
        result.Size.Should().BeNull();
        result.File.ContentType.Should().Be("application/octet-stream");
        stored.Should().Equal(ImageMetadataStripper.Strip(data));
        stored.Length.Should().BeLessThan(data.Length, "the EXIF segment must be gone");
    }

    [Fact]
    public async Task ShouldAcceptSquare8KImage()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("square.png", "image/png", TestImages.CreatePngHeader(7680, 7680));

        // act
        var result = await Process(upload);

        // assert
        result.Size.Should().Be(new Size2D(7680, 7680), "above ImageLimits.MaxPixelCount, but within the 12K bound");
    }

    [Fact]
    public async Task ShouldStoreUndecodableImageAsBinary()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.heic", "image/heic", "not an image"u8.ToArray());

        // act
        var result = await Process(upload);

        // assert
        result.File.ContentType.Should().Be("application/octet-stream");
    }

    // Private methods

    private async Task<ProcessedFile> Process(UploadedFile upload)
    {
        var result = await _processor.Process(upload, null, CancellationToken.None);
        _processedFiles.Add(result);
        return result;
    }

    private static async Task<byte[]> ReadAll(UploadedFile file)
    {
        var stream = await file.Open();
        await using var _ = stream;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
