using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class MediaKindProcessorSelectionTest : IDisposable
{
    private readonly MediaProcessor _mediaProcessor;
    private readonly List<ProcessedFile> _processedFiles = new();

    public MediaKindProcessorSelectionTest()
    {
        // Registration order matches CoreServerModule: Icon, then AttachmentImage, then legacy Image
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<RasterImageNormalizer>()
            .AddSingleton<IUploadProcessor, IconUploadProcessor>()
            .AddSingleton<IUploadProcessor, AttachmentImageUploadProcessor>()
            .AddSingleton<IUploadProcessor, ImageUploadProcessor>()
            .BuildServiceProvider();
        _mediaProcessor = new MediaProcessor(services);
    }

    public void Dispose()
    {
        foreach (var pf in _processedFiles)
            pf.DisposeSilently();
    }

    [Fact]
    public async Task ChatEntryAttachmentKindShouldKeepOriginalSize()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpeg(4000, 3000));

        // act
        var result = await Process(upload, MediaKind.ChatEntryAttachment);

        // assert
        result.Size.Should().Be(new Size2D(4000, 3000),
            "a media row reserved with MediaKind.ChatEntryAttachment must route to AttachmentImageUploadProcessor, which stores the client-provided size as-is");
    }

    [Fact]
    public async Task UnknownKindShouldDownscaleTo1920()
    {
        // arrange
        var upload = TestImages.CreateUploadedFile("photo.jpg", "image/jpeg", TestImages.CreateJpeg(4000, 3000));

        // act
        var result = await Process(upload, MediaKind.Unknown);

        // assert
        result.Size.Should().Be(new Size2D(1920, 1440),
            "a media row with no kind (e.g. reserved without MediaKind.ChatEntryAttachment) falls back to the legacy " +
            "ImageUploadProcessor, which resizes to 1920px regardless of the client's own preset");
    }

    private async Task<ProcessedFile> Process(UploadedFile upload, MediaKind mediaKind)
    {
        var result = await _mediaProcessor.ProcessUpload(upload, mediaKind, null, CancellationToken.None);
        _processedFiles.Add(result);
        return result;
    }
}
