using System.Net;
using System.Net.Http.Headers;
using ActualChat.Testing.Host;
using ActualChat.Uploads;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public class ContentControllerTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly byte[] HtmlBytes = "<html><body>content</body></html>"u8.ToArray();

    private IBlobStorage UploadBlobStorage =>
        field ??= AppHost.Services.BlobStorages()[BlobScope.UploadTempRecord];
    private IMediaSaver MediaSaver => field ??= AppHost.Services.GetRequiredService<IMediaSaver>();

    [Fact]
    public async Task DoesNotServeTempUploadScope()
    {
        // arrange
        var blobId = $"upload-temp/{Guid.NewGuid():N}";
        await UploadBlobStorage.Write(blobId, new MemoryStream(HtmlBytes), "text/html", CancellationToken.None);
        using var httpClient = AppHost.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync($"api/content/{blobId}");
        await UploadBlobStorage.Delete(blobId, CancellationToken.None);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DoesNotUseStoredHtmlContentType()
    {
        // arrange
        var blobId = await CreateMedia(HtmlBytes, "text/html", "");
        using var httpClient = AppHost.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync($"api/content/{blobId}");

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    [Fact]
    public async Task SendsNoSniffHeader()
    {
        // arrange
        var blobId = await CreateMedia(TestImages.CreatePng(10, 10), "image/png", "image.png");
        using var httpClient = AppHost.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync($"api/content/{blobId}");

        // assert
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
    }

    [Fact]
    public async Task ServesStoredImageInline()
    {
        // arrange
        var blobId = await CreateMedia(
            TestImages.CreatePng(10, 10),
            "application/octet-stream",
            "image.bin");
        using var httpClient = AppHost.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync($"api/content/{blobId}");

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    [Fact]
    public async Task ServesStoredPdfInline()
    {
        // arrange
        var pdfBytes = "%PDF-1.4\n1 0 obj\n<<>>\nendobj\n"u8.ToArray();
        var blobId = await CreateMedia(pdfBytes, "application/octet-stream", "doc.bin");
        using var httpClient = AppHost.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync($"api/content/{blobId}");

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    [Fact]
    public async Task ServesAttachmentWhenDownloadRequested()
    {
        // arrange
        var blobId = await CreateMedia(
            TestImages.CreatePng(10, 10),
            "application/octet-stream",
            "image.bin");
        using var httpClient = AppHost.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync($"api/content/{blobId}?download=1");

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Contain("image.bin");
    }

    [Fact]
    public async Task IgnoresUnrelatedQueryParameters()
    {
        // arrange
        var blobId = await CreateMedia(TestImages.CreatePng(10, 10), "application/octet-stream", "image.bin");
        using var httpClient = AppHost.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync($"api/content/{blobId}?v=123");

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    [Fact]
    public async Task ServesStoredAudioRangeInline()
    {
        // arrange
        var wavBytes = "RIFF\x24\0\0\0WAVEfmt \x10\0\0\0\x01\0\x01\0\x40\x1F\0\0\x40\x1F\0\0\x01\0\x08\0data\0\0\0\0"u8
            .ToArray();
        var blobId = await CreateMedia(wavBytes, "application/octet-stream", "audio.bin");
        using var httpClient = AppHost.NewHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/content/{blobId}");
        request.Headers.Range = new RangeHeaderValue(0, 3);

        // act
        using var response = await httpClient.SendAsync(request);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentType!.MediaType.Should().Be("audio/wav");
        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    [Fact]
    public async Task ServesStoredVideoInline()
    {
        // arrange
        var mp4Bytes = "\0\0\0\u0018ftypmp42\0\0\0\0mp42isom"u8.ToArray();
        var blobId = await CreateMedia(mp4Bytes, "application/octet-stream", "video.bin");
        using var httpClient = AppHost.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync($"api/content/{blobId}");

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("video/mp4");
        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    private async Task<string> CreateMedia(byte[] bytes, string contentType, string fileName)
    {
        var file = new UploadedStreamFile(
            fileName,
            contentType,
            bytes.Length,
            () => Task.FromResult<Stream>(new MemoryStream(bytes)));
        var mediaRef = await MediaSaver.Save(
            MediaId.New("content-controller-test"),
            file,
            null,
            MediaKind.ChatEntryAttachment,
            CancellationToken.None);
        return mediaRef.BlobId;
    }
}
