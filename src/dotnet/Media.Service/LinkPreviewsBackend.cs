using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Media.Db;
using ActualChat.Media.Module;
using ActualChat.Mesh;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

public class LinkPreviewsBackend(MediaSettings settings, IChatsBackend chatsBackend, IMediaBackend mediaBackend, IMarkupParser markupParser, Crawler crawler, IMeshLocks<MediaDbContext> meshLocks, IServiceProvider services)
    : DbServiceBase<MediaDbContext>(services), ILinkPreviewsBackend
{
    private IMeshLocks CrawlLocks { get; } = meshLocks.WithKeyPrefix(nameof(CrawlLocks));
    private Moment SystemNow => Clocks.SystemClock.Now;

    // [ComputeMethod]
    public virtual Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken)
        => GetAndRefreshIfRequired(id, null, true, cancellationToken);

    // [ComputeMethod]
    [Obsolete("2023.10: Remains for backward compability.")]
    public virtual async Task<LinkPreview?> GetForEntry(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        var entry = await chatsBackend.GetEntry(entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        var linkPreview = await Get(entry.LinkPreviewId, cancellationToken).ConfigureAwait(false);
        if (linkPreview != null)
            return linkPreview;

        // Regenerate link preview in background in case there is no preview yet (or it was wiped)
        _ = BackgroundTask.Run(() => GetOrGenerateForEntry(entry, CancellationToken.None), CancellationToken.None);
        return null;
    }

    // [CommandHandler]
    public virtual async Task<LinkPreview?> OnRefresh(LinkPreviewsBackend_Refresh command, CancellationToken cancellationToken)
    {
        var url = command.Url;
        var id = LinkPreview.ComposeId(url);
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var wasChanged = context.Operation.Items.GetOrDefault(false);
            if (wasChanged)
                _ = GetFromDb(id, default);
            return default!;
        }

        if (id.IsEmpty)
            return null;

        var runOptions = new RunLockedOptions(3, RetryDelaySeq.Exp(1.5, 5), Log);
        var resultOpt = await CrawlLocks
            .TryRunLocked(id, runOptions, RefreshTaskFactory, cancellationToken)
            .ConfigureAwait(false);
        return resultOpt.IsSome(out var result)
            ? result
            : LinkPreview.UseExisting;

        async Task<LinkPreview> RefreshTaskFactory(CancellationToken ct)
            => await RefreshUnsafe(id, url, ct).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind) = eventCommand;
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        if (changeKind is ChangeKind.Remove)
            return Task.CompletedTask;

        return GetOrGenerateForEntry(entry, cancellationToken);
    }

    // Private methods

    private async Task<LinkPreview> RefreshUnsafe(Symbol id, string url, CancellationToken cancellationToken)
    {
        var linkMeta = await crawler.Crawl(url, cancellationToken).ConfigureAwait(false);

        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbLinkPreview = await dbContext.LinkPreviews.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        if (dbLinkPreview != null && SystemNow - dbLinkPreview.ModifiedAt.ToMoment() < TimeSpan.FromDays(1))
            return dbLinkPreview.ToModel();

        var videoMeta = linkMeta.OpenGraph.Video;
        if (dbLinkPreview == null) {
            var linkPreview = new LinkPreview {
                Id = LinkPreview.ComposeId(url),
                Version = VersionGenerator.NextVersion(),
                Url = url,
                Title = linkMeta.OpenGraph.Title,
                Description = linkMeta.OpenGraph.Description,
                PreviewMediaId = linkMeta.PreviewMediaId,
                CreatedAt = SystemNow,
                ModifiedAt = SystemNow,
            };
            if (videoMeta != OpenGraphVideo.None)
                linkPreview = linkPreview with {
                    VideoSite = linkMeta.OpenGraph.SiteName,
                    VideoUrl = linkMeta.OpenGraph.Video.SecureUrl,
                    VideoWidth = linkMeta.OpenGraph.Video.Width,
                    VideoHeight = linkMeta.OpenGraph.Video.Height,
                };

            dbLinkPreview = new DbLinkPreview(linkPreview);
            dbContext.Add(dbLinkPreview);
        }
        else {
            dbLinkPreview = await dbContext.LinkPreviews.ForNoKeyUpdate()
                .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
                .ConfigureAwait(false);
            var linkPreview = dbLinkPreview!.ToModel();
            if (linkMeta.OpenGraph != OpenGraph.None)
                linkPreview = linkPreview with {
                    Title = linkMeta.OpenGraph.Title,
                    Description = linkMeta.OpenGraph.Description,
                };
            if (videoMeta != OpenGraphVideo.None)
                linkPreview = linkPreview with {
                    VideoSite = linkMeta.OpenGraph.SiteName,
                    VideoUrl = videoMeta.SecureUrl,
                    VideoWidth = videoMeta.Width,
                    VideoHeight = videoMeta.Height,
                };
            linkPreview = linkPreview with {
                PreviewMediaId = linkMeta.PreviewMediaId,
                ModifiedAt = SystemNow,
                Version = VersionGenerator.NextVersion(linkPreview.Version),
            };
            dbLinkPreview.UpdateFrom(linkPreview);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set(true);
        return dbLinkPreview.ToModel();
    }

    private async Task<LinkPreview?> GetOrGenerateForEntry(ChatEntry entry, CancellationToken cancellationToken)
    {
        var linkPreview = await GetAndRefreshIfRequired(entry, false, cancellationToken).ConfigureAwait(false);
        var linkPreviewId = linkPreview?.Id ?? Symbol.Empty;
        Log.LogDebug("Retrieved LinkPreview #{LinkPreviewId} for entry #{EntryId}. Current Entry.LinkPreview: #{PreviousLinkPreviewId}", linkPreviewId, entry.Id, entry.LinkPreviewId);
        if (entry.LinkPreviewId == linkPreviewId)
            return linkPreview;

        Log.LogDebug("Setting LinkPreview #{LinkPreviewId} for entry #{EntryId}", linkPreviewId, entry.Id);
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

        var linkPreview = await GetFromDb(id, cancellationToken).ConfigureAwait(false);
        url ??= linkPreview?.Url;
        var mustRefresh = !url.IsNullOrEmpty()
            && (linkPreview == null || linkPreview.ModifiedAt + settings.LinkPreviewUpdatePeriod < SystemNow);
        if (mustRefresh) {
            if (await IsCrawling(id, cancellationToken).ConfigureAwait(false))
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
        if (linkPreview?.PreviewMediaId.IsNone != false)
            return linkPreview;

        return linkPreview with {
            PreviewMedia = await mediaBackend.Get(linkPreview.PreviewMediaId, cancellationToken).ConfigureAwait(false),
        };
    }

    [ComputeMethod]
    protected virtual async Task<LinkPreview?> GetFromDb(Symbol id, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbLinkPreview = await dbContext.LinkPreviews
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        return dbLinkPreview?.ToModel();
    }

    private HashSet<string> ExtractUrls(ChatEntry entry)
    {
        var markup = markupParser.Parse(entry.Content);
        return new LinkExtractor().GetLinks(markup);
    }

    // redis helpers

    private async Task<bool> IsCrawling(Symbol id, CancellationToken cancellationToken)
        => await CrawlLocks.GetInfo(id, cancellationToken).ConfigureAwait(false) != null;
}
