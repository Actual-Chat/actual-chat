using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Media.Db;
using ActualChat.Media.Module;
using ActualChat.Mesh;
using ActualChat.Redis;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

public class LinkPreviewsBackend(IServiceProvider services)
    : DbServiceBase<MediaDbContext>(services), ILinkPreviewsBackend
{
    private const string RedisKeyPrefix = ".LinkCrawlerLocks.";

    // all backend services should be requested lazily to avoid circular references!
    private IMediaBackend? _mediaBackend;
    private IChatsBackend? _chatsBackend;
    private Crawler? _crawler;
    private IMeshLocks<MediaDbContext>? _meshLocks;
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IMediaBackend MediaBackend => _mediaBackend ??= Services.GetRequiredService<IMediaBackend>();
    private IMeshLocks<MediaDbContext> MeshLocks => _meshLocks ??= Services.GetRequiredService<IMeshLocks<MediaDbContext>>();

    private MediaSettings Settings { get; } = services.GetRequiredService<MediaSettings>();
    private IMarkupParser MarkupParser { get; } = services.GetRequiredService<IMarkupParser>();
    private Crawler Crawler => _crawler ??= Services.GetRequiredService<Crawler>();
    private Moment SystemNow => Clocks.SystemClock.Now;

    // [ComputeMethod]
    public virtual Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
        => GetAndRefreshIfRequired(id, null, true, cancellationToken);

    // [ComputeMethod]
    [Obsolete("2023.10: Remains for backward compability.")]
    public virtual async Task<LinkPreview?> GetForEntry(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        var entry = await ChatsBackend.GetEntry(entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        var linkPreview = await Get(entry.LinkPreviewId, cancellationToken).ConfigureAwait(false);
        if (linkPreview != null)
            return linkPreview;

        // Regenerate link preview in background in case there is no preview yet (or it was wiped)
        _ = BackgroundTask.Run(() => GenerateForEntry(entry, CancellationToken.None), CancellationToken.None);
        return null;
    }

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

        if (id.IsEmpty)
            return null;

        var (wasRun, result) = await MeshLocks
            .TryRun(ct => RefreshUnsafe(id, url, ct), ToRedisKey(id), cancellationToken)
            .ConfigureAwait(false);
        return !wasRun ? LinkPreview.UseExisting : result;
    }

    // [EventHandler]
    public virtual Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind) = eventCommand;
        if (Computed.IsInvalidating())
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        if (changeKind is ChangeKind.Remove)
            return Task.CompletedTask;

        return GenerateForEntry(entry, cancellationToken);
    }

    // Private methods

    private async Task<LinkPreview> RefreshUnsafe(Symbol id, string url, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbLinkPreview = await dbContext.LinkPreviews.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        if (dbLinkPreview != null && SystemNow - dbLinkPreview.ModifiedAt.ToMoment() < TimeSpan.FromDays(1))
            return dbLinkPreview.ToModel();

        var linkMeta = await Crawler.Crawl(url, cancellationToken).ConfigureAwait(false);
        if (dbLinkPreview == null) {
            var linkPreview = new LinkPreview {
                Id = LinkPreview.ComposeId(url),
                Version = VersionGenerator.NextVersion(),
                Url = url,
                Title = linkMeta.Title,
                Description = linkMeta.Description,
                PreviewMediaId = linkMeta.PreviewMediaId,
                CreatedAt = SystemNow,
                ModifiedAt = SystemNow,
            };
            if (!linkMeta.VideoMetadata.IsNone)
                linkPreview = linkPreview with {
                    VideoSite = linkMeta.VideoMetadata.SiteName,
                    VideoUrl = linkMeta.VideoMetadata.SecureUrl,
                    VideoWidth = linkMeta.VideoMetadata.Width,
                    VideoHeight = linkMeta.VideoMetadata.Height,
                };

            dbLinkPreview = new DbLinkPreview(linkPreview);
            dbContext.Add(dbLinkPreview);
        }
        else {
            dbLinkPreview = await dbContext.LinkPreviews.ForNoKeyUpdate()
                .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
                .ConfigureAwait(false);
            var linkPreview = dbLinkPreview!.ToModel();
            linkPreview = linkPreview with {
                PreviewMediaId = linkMeta.PreviewMediaId,
                Title = linkMeta.Title,
                Description = linkMeta.Description,
                ModifiedAt = SystemNow,
                Version = VersionGenerator.NextVersion(linkPreview.Version),
            };
            if (!linkMeta.VideoMetadata.IsNone)
                linkPreview = linkPreview with {
                    VideoSite = linkMeta.VideoMetadata.SiteName,
                    VideoUrl = linkMeta.VideoMetadata.SecureUrl,
                    VideoWidth = linkMeta.VideoMetadata.Width,
                    VideoHeight = linkMeta.VideoMetadata.Height,
                };
            dbLinkPreview.UpdateFrom(linkPreview);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(true);
        return dbLinkPreview.ToModel();
    }

    private async Task<LinkPreview?> GenerateForEntry(ChatEntry entry, CancellationToken cancellationToken)
    {
        var linkPreview = await GetAndRefreshIfRequired(entry, false, cancellationToken).ConfigureAwait(false);
        var linkPreviewId = linkPreview?.Id ?? Symbol.Empty;
        if (entry.LinkPreviewId == linkPreviewId)
            return linkPreview;

        var changeTextEntryCmd = new ChatsBackend_ChangeEntry(
            entry.Id,
            null, // The entry passed here may have an outdated version
            Change.Update(new ChatEntryDiff { LinkPreviewId = linkPreviewId }));
        await Commander.Call(changeTextEntryCmd, true, cancellationToken).ConfigureAwait(false);
        return linkPreview;
    }

    private async Task<LinkPreview?> GetAndRefreshIfRequired(ChatEntry entry, bool allowStale, CancellationToken cancellationToken)
    {
        var urls = ExtractUrls(entry);
        foreach (var url in urls) {
            var preview = await GetAndRefreshIfRequired(LinkPreview.ComposeId(url), url, allowStale, cancellationToken).ConfigureAwait(false);
            if (preview != null)
                return preview;
        }
        return null;
    }

    private async Task<LinkPreview?> GetAndRefreshIfRequired(Symbol id, string? url, bool allowStale, CancellationToken cancellationToken)
    {
        if (id.IsEmpty)
            return null;

        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbLinkPreview = await dbContext.LinkPreviews
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        var linkPreview = dbLinkPreview?.ToModel();
        url ??= linkPreview?.Url;
        var mustRefresh = !url.IsNullOrEmpty()
            && (linkPreview == null || linkPreview.ModifiedAt + Settings.LinkPreviewUpdatePeriod < SystemNow);
        if (mustRefresh) {
            if (await IsAlreadyCrawling(id, cancellationToken).ConfigureAwait(false))
                return linkPreview;

            // Intentionally not passing CancellationToken to avoid cancellation on exit
            var refreshTask = Commander.Call(new LinkPreviewsBackend_Refresh(url!), CancellationToken.None);
            if (!allowStale)
                linkPreview = await refreshTask.ConfigureAwait(false);
            else if (linkPreview == null)
                linkPreview = LinkPreview.Updating;
            else if (linkPreview == LinkPreview.UseExisting)
                return linkPreview;
        }
        if (linkPreview == null || linkPreview.PreviewMediaId.IsNone)
            return linkPreview;

        return linkPreview with {
            PreviewMedia = await MediaBackend.Get(linkPreview.PreviewMediaId, cancellationToken)
                .ConfigureAwait(false),
        };
    }

    private HashSet<string> ExtractUrls(ChatEntry entry)
    {
        var markup = MarkupParser.Parse(entry.Content);
        return new LinkExtractor().GetLinks(markup);
    }

    // redis helpers

    private static string ToRedisKey(Symbol id)
        => $"{RedisKeyPrefix}{id.Value}";

    private async Task<bool> IsAlreadyCrawling(Symbol id, CancellationToken cancellationToken)
        => await MeshLocks.GetInfo(ToRedisKey(id), cancellationToken).ConfigureAwait(false) != null;
}
