using ActualChat.Chat;
using ActualChat.Media.Module;
using ActualChat.Uploads;
using OpenGraphNet;
using OpenGraphNet.Metadata;
using FileInfo = ActualChat.Uploads.FileInfo;

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

        var fileInfo = await DownloadImageToFile(imageUri, cancellationToken).ConfigureAwait(false);
        if (fileInfo is null)
            return MediaId.None;

        var mediaId = new MediaId(imageUri.AbsoluteUri.GetSHA256HashCode(HashEncoding.AlphaNumeric),
            fileInfo.Content.GetSHA256HashCode(HashEncoding.AlphaNumeric));
        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is not null)
            return media.Id;

        // TODO: extract common part with ChatMediaController
        var (processedFile, size) = await ProcessFile(fileInfo, cancellationToken).ConfigureAwait(false);
        media = new Media(mediaId) {
            ContentId = $"media/{mediaId.LocalId}/{fileInfo.FileName}",
            FileName = fileInfo.FileName,
            Length = fileInfo.Length,
            ContentType = fileInfo.ContentType,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0,
        };

        using var stream = new MemoryStream(processedFile.Content);
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

    private async Task<FileInfo?> DownloadImageToFile(Uri imageUri, CancellationToken cancellationToken)
    {
        var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(Settings.CrawlerImageDownloadTimeout);

        var response = await HttpClient.GetAsync(imageUri, cts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var ext = MediaTypeExt.GetFileExtension(contentType);
        if (ext.IsNullOrEmpty())
            return null;

        var imageBytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
        var fileName = new string(imageUri.Segments[^1].Where(Alphabet.AlphaNumeric.IsMatch).ToArray());
        fileName = Path.ChangeExtension(fileName, ext);
        return new FileInfo(fileName, contentType, imageBytes.Length, imageBytes);
    }

    private Task<ProcessedFileInfo> ProcessFile(FileInfo file, CancellationToken cancellationToken)
    {
        var processor = UploadProcessors.FirstOrDefault(x => x.Supports(file));
        return processor != null
            ? processor.Process(file, cancellationToken)
            : Task.FromResult(new ProcessedFileInfo(file, null));
    }
}

public sealed record CrawledLink(string Title, string Description, MediaId PreviewMediaId)
{
    public static readonly CrawledLink None = new ("", "", MediaId.None);
}
