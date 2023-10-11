using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Chat.Module;
using ActualChat.Commands;
using ActualChat.Media;
using Microsoft.EntityFrameworkCore;
using OpenGraphNet;
using OpenGraphNet.Metadata;
using StackExchange.Redis;
using Stl.Fusion.EntityFramework;
using Stl.Redis;

namespace ActualChat.Chat;

public class LinkPreviewsBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), ILinkPreviewsBackend
{
    private static readonly Dictionary<string, string> ImageExtensionByContentType =
        new (StringComparer.OrdinalIgnoreCase) {
            ["image/bmp"] = ".bmp",
            ["image/jpeg"] = ".jpg",
            ["image/vnd.microsoft.icon"] = ".ico",
            ["image/png"] = ".png",
            ["image/svg+xml"] = ".svg",
            ["image/webp"] = ".webp",
        };
    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();
    private IMarkupParser MarkupParser { get; } = services.GetRequiredService<IMarkupParser>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private HttpClient HttpClient { get; } = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LinkPreviewsBackend));
    private IReadOnlyCollection<IUploadProcessor> UploadProcessors { get; } = services.GetRequiredService<IEnumerable<IUploadProcessor>>().ToList();
    private IContentSaver ContentSaver { get; } = services.GetRequiredService<IContentSaver>();
    private RedisDb<ChatDbContext> RedisDb { get; } = services.GetRequiredService<RedisDb<ChatDbContext>>();
    private Moment Now => Clocks.SystemClock.Now;

    // [ComputeMethod]
    public virtual Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
        => Get(id, null, false, cancellationToken);

    // [CommandHandler]
    public virtual async Task<LinkPreview?> OnRefresh(LinkPreviewsBackend_Refresh command, CancellationToken cancellationToken)
    {
        var url = command.Url;
        var id = LinkPreview.ComposeId(url);
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var wasChanged = context.Operation().Items.GetOrDefault(false);
            if (wasChanged)
                _ = Get(id, default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbLinkPreview = await dbContext.LinkPreviews.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        if (dbLinkPreview != null && Now - dbLinkPreview.ModifiedAt.ToMoment() < TimeSpan.FromDays(1))
            return dbLinkPreview.ToModel();

        var canCrawl = await RedisDb.Database
            .StringSetAsync(id.Value, Now.ToString(), Settings.CrawlingTimeout, When.NotExists)
            .ConfigureAwait(false);
        if (!canCrawl)
            // crawling of this url is already in progress
            return null!;

        var linkMeta = await Crawl(url, cancellationToken).ConfigureAwait(false);
        if (dbLinkPreview == null) {
            dbLinkPreview = new DbLinkPreview {
                Id = LinkPreview.ComposeId(url),
                Url = url,
                Title = linkMeta.Title,
                Description = linkMeta.Description,
                ThumbnailMediaId = linkMeta.PreviewMediaId,
                CreatedAt = Now,
                ModifiedAt = Now,
            };
            dbContext.Add(dbLinkPreview);
        }
        else {
            dbLinkPreview.ThumbnailMediaId = linkMeta.PreviewMediaId;
            dbLinkPreview.Title = linkMeta.Title;
            dbLinkPreview.Description = linkMeta.Description;
            dbLinkPreview.ModifiedAt = Now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(true);
        return dbLinkPreview.ToModel();
    }

    // Events

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind) = eventCommand;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invPreviewId = context.Operation().Items.GetOrDefault("");
            if (!invPreviewId.IsNullOrEmpty())
                _ = Get(LinkPreview.ComposeId(invPreviewId), default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var prevPreviewId = entry.LinkPreviewId;
        if(changeKind is ChangeKind.Create or ChangeKind.Update) {
            var preview = await FetchPreview(entry, cancellationToken).ConfigureAwait(false);
            if (entry.LinkPreviewId != preview.Id && !preview.IsEmpty)
                entry = entry with { LinkPreviewId = preview.Id };
        }

        if (prevPreviewId == entry.LinkPreviewId)
            return;

        context.Operation().Items.Set(entry.LinkPreviewId.Value);
        if (changeKind != ChangeKind.Remove) {
            var changeTextEntryCmd = new ChatsBackend_UpsertEntry(entry, entry.Attachments.Count > 0);
            await Commander.Call(changeTextEntryCmd, true, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task<LinkPreview> FetchPreview(ChatEntry entry, CancellationToken cancellationToken)
    {
        var urls = ExtractUrls(entry);
        foreach (var url in urls) {
            var preview = await Get(LinkPreview.ComposeId(url), url, true, cancellationToken)
                .ConfigureAwait(false);
            if (preview != null)
                return preview;
        }
        return LinkPreview.None;
    }

    private async Task<LinkPreview?> Get(Symbol id, string? url, bool wait, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbLinkPreview = await dbContext.LinkPreviews.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        var linkPreview = dbLinkPreview?.ToModel();
        var ensureRefreshedTask = EnsureRefreshed();
        if (wait)
            linkPreview = await ensureRefreshedTask.ConfigureAwait(false);

        if (linkPreview?.PreviewMediaId.IsNone != false)
            return linkPreview;

        return linkPreview with {
            PreviewMedia = await MediaBackend.Get(linkPreview.PreviewMediaId, cancellationToken)
                .ConfigureAwait(false),
        };

        Task<LinkPreview?> EnsureRefreshed()
        {
            if (linkPreview != null && Now - linkPreview.ModifiedAt < Settings.LinkPreviewUpdatePeriod)
                return Task.FromResult<LinkPreview?>(linkPreview);

            url ??= linkPreview?.Url;
            if (url.IsNullOrEmpty())
                return Task.FromResult(linkPreview);

            return Commander.Call(new LinkPreviewsBackend_Refresh(url), cancellationToken);
        }
    }

    private HashSet<string> ExtractUrls(ChatEntry entry)
    {
        var markup = MarkupParser.Parse(entry.Content);
        return new LinkExtractor().GetLinks(markup);
    }

    private async Task<LinkMeta> Crawl(string url, CancellationToken cancellationToken)
    {
        try {
            var response = await HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return LinkMeta.None;

            // TODO: consider robot tags

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (OrdinalIgnoreCaseEquals(contentType, "text/html"))
                return await CrawlWebSite(url, cancellationToken).ConfigureAwait(false);

            if (contentType.OrdinalIgnoreCaseStartsWith("image/"))
                return await CrawlImageLink(url, cancellationToken).ConfigureAwait(false);

            // TODO: support more cases
        }
        catch (Exception e) {
            Log.LogDebug(e, "Failed to crawl link {url}", url);
        }
        return LinkMeta.None;
    }

    private async Task<LinkMeta> CrawlWebSite(string url, CancellationToken cancellationToken)
    {
        var graph = await OpenGraph.ParseUrlAsync(url, timeout: (int)Settings.CrawlerGraphParsingTimeout.TotalMilliseconds, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        // TODO: limit image size limit
        var mediaId = await GrabImage(graph.Image, cancellationToken).ConfigureAwait(false);
        var description = graph.Metadata["og:description"].Value();
        if (!description.IsNullOrEmpty())
            description = description.HtmlDecode();
        return new (graph.Title.HtmlDecode(), description, mediaId);
    }

    private async Task<LinkMeta> CrawlImageLink(string url, CancellationToken cancellationToken)
    {
        // TODO: limit image size limit
        var mediaId = await GrabImage(new Uri(url), cancellationToken).ConfigureAwait(false);
        return new LinkMeta ("", "", mediaId);
    }

    private async Task<MediaId> GrabImage(Uri? imageUri, CancellationToken cancellationToken)
    {
        if (imageUri is null)
            return MediaId.None;

        var fileInfo = await FetchImageFile().ConfigureAwait(false);
        if (fileInfo is null)
            return MediaId.None;

        var mediaId = new MediaId(imageUri.AbsoluteUri.GetSHA256HashCode(HashEncoding.AlphaNumeric), fileInfo.Content.GetSHA256HashCode(HashEncoding.AlphaNumeric));
        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is not null)
            return media.Id;
        // TODO: extract common part with ChatMediaController
        var (processedFile, size) = await ProcessFile(fileInfo, cancellationToken).ConfigureAwait(false);
        media = new Media.Media(mediaId) {
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
            new Change<Media.Media> {
                Create = media,
            });
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        return mediaId;

        async Task<FileInfo?> FetchImageFile()
        {
            var cts = cancellationToken.CreateLinkedTokenSource();
            cts.CancelAfter(Settings.CrawlerImageDownloadTimeout);

            var response = await HttpClient.GetAsync(imageUri, cts.Token).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!ImageExtensionByContentType.TryGetValue(contentType, out var ext))
                return null;

            var imageBytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            var fileName = Path.ChangeExtension(imageUri.Segments[^1], ext);
            return new FileInfo(fileName, contentType, imageBytes.Length, imageBytes);
        }
    }

    private Task<ProcessedFileInfo> ProcessFile(FileInfo file, CancellationToken cancellationToken)
    {
        var processor = UploadProcessors.FirstOrDefault(x => x.Supports(file));
        return processor != null
            ? processor.Process(file, cancellationToken)
            : Task.FromResult(new ProcessedFileInfo(file, null));
    }

    private sealed record LinkMeta(string Title, string Description, MediaId PreviewMediaId)
    {
        public static readonly LinkMeta None = new ("", "", MediaId.None);
    }
}
