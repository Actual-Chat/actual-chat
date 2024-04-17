using System.Text;
using ActualChat.Hashing;
using ActualChat.Media.Module;
using ActualChat.Uploads;
using ActualLab.IO;

namespace ActualChat.Media;

public sealed class Crawler(
    MediaSettings settings,
    IMediaBackend mediaBackend,
    IContentSaver contentSaver,
    IHttpClientFactory httpClientFactory,
    ICommander commander,
    ILogger<Crawler> log,
    IEnumerable<IUploadProcessor> uploadProcessors)
{
    private HttpClient HttpClient { get; } = httpClientFactory.CreateClient(nameof(LinkPreviewsBackend));
    private HttpClient FallbackHttpClient { get; } = httpClientFactory.CreateClient(nameof(LinkPreviewsBackend) + ".fallback");
    private IReadOnlyList<IUploadProcessor> UploadProcessors => uploadProcessors.ToList();

    public async Task<CrawledLink> Crawl(string url, CancellationToken cancellationToken)
    {
        try {
            var response = await HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return CrawledLink.None;

            // TODO: consider robot tags

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (OrdinalIgnoreCaseEquals(contentType, "text/html"))
                return await CrawlWebSite(url, cancellationToken).ConfigureAwait(false);

            if (contentType.OrdinalIgnoreCaseStartsWith("image/"))
                return await CrawlImageLink(url, cancellationToken).ConfigureAwait(false);

            // TODO: support more cases
        }
        catch (Exception e) {
            log.LogDebug(e, "Failed to crawl link {Url}", url);
        }
        return CrawledLink.None;
    }

    private async Task<CrawledLink> CrawlWebSite(string url, CancellationToken cancellationToken)
    {
        var graph = await ParseUrl(url, cancellationToken).ConfigureAwait(false);
        if (graph is null)
            return CrawledLink.None;

        var mediaId = await GrabImage(graph.ImageUrl, cancellationToken).ConfigureAwait(false);
        return new (mediaId, graph);
    }

    private async Task<OpenGraph?> ParseUrl(string url, CancellationToken cancellationToken)
    {
        var openGraph = await GetGraph(HttpClient).ConfigureAwait(false);
        return openGraph ?? await GetGraph(FallbackHttpClient).ConfigureAwait(false);

        async Task<OpenGraph?> GetGraph(HttpClient httpClient)
        {
            using var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(settings.GraphParseTimeout);
            var html = await httpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);
            return OpenGraphParser.Parse(html);
        }
    }

    private async Task<CrawledLink> CrawlImageLink(string url, CancellationToken cancellationToken)
    {
        // TODO: image size limit
        var mediaId = await GrabImage(url, cancellationToken).ConfigureAwait(false);
        return new CrawledLink(mediaId, OpenGraph.None);
    }

    private async Task<MediaId> GrabImage(string imageUri, CancellationToken cancellationToken)
    {
        if (imageUri.IsNullOrEmpty())
            return MediaId.None;

        // TODO: image size limit
        var processedFile = await DownloadImageToFile(imageUri, cancellationToken).ConfigureAwait(false);
        if (processedFile is null)
            return MediaId.None;

        var mediaIdScope = imageUri.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
        var mediaLid = await processedFile.File.Process(async stream => {
            var hash = await stream.Hash().SHA256(cancellationToken).ConfigureAwait(false);
            return hash.AlphaNumeric();
        }).ConfigureAwait(false);
        var mediaId = new MediaId(mediaIdScope, mediaLid);
        var media = await mediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is not null)
            return media.Id;

        // TODO: extract common part with ChatMediaController
        media = new Media(mediaId) {
            ContentId = $"media/{mediaId.LocalId}/{processedFile.File.FileName}",
            FileName = processedFile.File.FileName,
            Length = processedFile.File.Length,
            ContentType = processedFile.File.ContentType,
            Width = processedFile.Size?.Width ?? 0,
            Height = processedFile.Size?.Height ?? 0,
        };

        var stream = await processedFile.File.Open().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        var content = new Content(media.ContentId, media.ContentType, stream);
        await contentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        var changeCommand = new MediaBackend_Change(
            mediaId,
            new Change<Media> {
                Create = media,
            });
        await commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        return mediaId;
    }

    private async Task<ProcessedFile?> DownloadImageToFile(string imageUrl, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(settings.ImageDownloadTimeout);
        return await Download(cts.Token).ConfigureAwait(false);

        async Task<ProcessedFile?> Download(CancellationToken cancellationToken1)
        {
            var response = await HttpClient.GetAsync(imageUrl, cancellationToken1).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var ext = MediaTypeExt.GetFileExtension(contentType); // TODO: convert if icon is not supported
            if (ext.IsNullOrEmpty())
                return null;

            var lastSegment = response.RequestMessage!.RequestUri!.Segments[^1].TrimSuffix(ext);
            FilePath fileName = new string(lastSegment.Where(Alphabet.AlphaNumeric.IsMatch).ToArray()) + ext;
            var file = new UploadedStreamFile(fileName, contentType, response.Content.Headers.ContentLength ?? 0, () => response.Content.ReadAsStreamAsync(cancellationToken1));
            return await UploadProcessors.Process(file, cancellationToken1).ConfigureAwait(false);
        }
    }
}
