using ActualChat.Media.Module;
using ActualChat.Uploads;
using OpenGraphNet;
using OpenGraphNet.Metadata;
using Stl.IO;

namespace ActualChat.Media;

public class Crawler(IServiceProvider services) : IHasServices
{
    private IContentSaver? _contentSaver;
    private MediaSettings? _settings;
    private IMediaBackend? _mediaBackend;
    private IReadOnlyCollection<IUploadProcessor>? _uploadProcessors;
    private ICommander? _commander;
    private ILogger? _log;

    public IServiceProvider Services { get; } = services;
    private MediaSettings Settings => _settings ??= Services.GetRequiredService<MediaSettings>();
    private IMediaBackend MediaBackend => _mediaBackend ??= Services.GetRequiredService<IMediaBackend>();
    private HttpClient HttpClient { get; } = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LinkPreviewsBackend));
    private IReadOnlyCollection<IUploadProcessor> UploadProcessors => _uploadProcessors ??= Services.GetRequiredService<IEnumerable<IUploadProcessor>>().ToList();
    private IContentSaver ContentSaver => _contentSaver ??= Services.GetRequiredService<IContentSaver>();
    private ICommander Commander => _commander ??= Services.Commander();
    private ILogger Log => _log ??= Services.LogFor(GetType());

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
            Log.LogDebug(e, "Failed to crawl link {Url}", url);
        }
        return CrawledLink.None;
    }

    private async Task<CrawledLink> CrawlWebSite(string url, CancellationToken cancellationToken)
    {
        var graph = await OpenGraph.ParseUrlAsync(url,
                timeout: (int)Settings.CrawlerGraphParsingTimeout.TotalMilliseconds,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        // TODO: image size limit
        var mediaId = await GrabImage(graph.Image, cancellationToken).ConfigureAwait(false);
        var description = graph.Metadata["og:description"].Value();
        if (!description.IsNullOrEmpty())
            description = description.HtmlDecode();
        return new (graph.Title.HtmlDecode(), description, mediaId);
    }

    private async Task<CrawledLink> CrawlImageLink(string url, CancellationToken cancellationToken)
    {
        // TODO: image size limit
        var mediaId = await GrabImage(new Uri(url), cancellationToken).ConfigureAwait(false);
        return new CrawledLink("", "", mediaId);
    }

    private async Task<MediaId> GrabImage(Uri? imageUri, CancellationToken cancellationToken)
    {
        if (imageUri is null)
            return MediaId.None;

        var processedFile = await DownloadImageToFile(imageUri, cancellationToken).ConfigureAwait(false);
        if (processedFile is null)
            return MediaId.None;

        var mediaIdScope = imageUri.AbsoluteUri.GetSHA256HashCode(HashEncoding.AlphaNumeric);
        var mediaLid = await processedFile.File.GetSHA256HashCode(HashEncoding.AlphaNumeric).ConfigureAwait(false);
        var mediaId = new MediaId(mediaIdScope, mediaLid);
        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
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
        await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        var changeCommand = new MediaBackend_Change(
            mediaId,
            new Change<Media> {
                Create = media,
            });
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        return mediaId;
    }

    private Task<ProcessedFile?> DownloadImageToFile(Uri imageUri, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(Settings.CrawlerImageDownloadTimeout);
        return Download(cts.Token);

        async Task<ProcessedFile?> Download(CancellationToken cancellationToken1)
        {
            var response = await HttpClient.GetAsync(imageUri, cancellationToken1).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            FilePath fileName = new string(response.RequestMessage!.RequestUri!.Segments[^1].Where(Alphabet.AlphaNumeric.IsMatch).ToArray());
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var ext = MediaTypeExt.GetFileExtension(contentType); // TODO: convert if icon is not supported
            if (ext.IsNullOrEmpty())
                return null;

            var file = new UploadedStreamFile(fileName, contentType, response.Content.Headers.ContentLength ?? 0, () => response.Content.ReadAsStreamAsync(cancellationToken1));
            return await UploadProcessors.Process(file, cancellationToken1).ConfigureAwait(false);
        }
    }
}

public sealed record CrawledLink(string Title, string Description, MediaId PreviewMediaId)
{
    public static readonly CrawledLink None = new ("", "", MediaId.None);
}
