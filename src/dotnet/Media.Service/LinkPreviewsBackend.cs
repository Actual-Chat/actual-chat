using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Media.Db;
using ActualChat.Media.Flows;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

/// <summary>
/// Backend service implementation for generating and caching URL link previews.
/// </summary>
public class LinkPreviewsBackend(IServiceProvider services)
    : DbServiceBase<MediaDbContext>(services), ILinkPreviewsBackend
{
    private IMarkupParser MarkupParser => field ??= Services.GetRequiredService<IMarkupParser>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private IDbEntityResolver<string, DbLinkPreview> EntityResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbLinkPreview>>();
    private FlowHub FlowHub => field ??= Services.FlowHub();
    private Moment SystemNow => Clocks.SystemClock.Now;

    // [ComputeMethod]
    public virtual async Task<LinkPreview?> Get(
        Symbol id, bool tryScheduleRefresh, CancellationToken cancellationToken)
    {
        var dbLinkPreview = await EntityResolver.Get(id, cancellationToken).ConfigureAwait(false);
        var linkPreview = dbLinkPreview?.ToModel();

        if (tryScheduleRefresh && linkPreview != null)
            await FlowHub.TryScheduleUpdate<LinkPreviewFlow>(linkPreview.Url, cancellationToken).ConfigureAwait(false);
        if (linkPreview == null || linkPreview.PreviewMediaId == null)
            return linkPreview;

        return linkPreview with {
            PreviewMedia = await MediaBackend.Get(linkPreview.PreviewMediaId, cancellationToken).ConfigureAwait(false),
        };
    }

    // [CommandHandler]
    public virtual async Task<LinkPreview?> OnChange(LinkPreviewsBackend_Change command, CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = Get(id, false, default);
            _ = Get(id, true, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.LinkPreviews.LockShared(id, cancellationToken).ConfigureAwait(false);
        var dbLinkPreview = await dbContext.LinkPreviews.GetAsNoTracking(id, cancellationToken).ConfigureAwait(false);

        if (change.IsCreate(out var linkPreview)) {
            if (dbLinkPreview != null)
                return dbLinkPreview.ToModel();

            await dbContext.LinkPreviews.Lock(id, cancellationToken).ConfigureAwait(false);
            dbLinkPreview = new DbLinkPreview(linkPreview) {
                Id = id,
                CreatedAt = SystemNow,
                ModifiedAt = SystemNow,
                Version = VersionGenerator.NextVersion(),
            };
            dbContext.Add(dbLinkPreview);
        } else if (change.IsUpdate(out linkPreview)) {
            if (dbLinkPreview is null)
                return null;

            dbLinkPreview.RequireVersion(expectedVersion);
            dbContext.LinkPreviews.Attach(dbLinkPreview);
            linkPreview = linkPreview with {
                CreatedAt = dbLinkPreview.CreatedAt,
                Version = VersionGenerator.NextVersion(dbLinkPreview.Version),
                ModifiedAt = SystemNow,
            };
            dbLinkPreview.UpdateFrom(linkPreview);
        }
        else
            throw StandardError.NotSupported("Link previews cannot be removed.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbLinkPreview.ToModel();
    }

    // [EventHandler]
    public virtual Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind, oldEntry) = eventCommand;
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        return ScheduleNewLinksCrawling();

        async Task ScheduleNewLinksCrawling() {
            if (changeKind is ChangeKind.Remove)
                return;

            var links = ExtractLinks(entry);
            var oldLinks = ExtractLinks(oldEntry);
            foreach (var link in links.Except(oldLinks))
                await FlowHub.TryScheduleUpdate<LinkPreviewFlow>(link, cancellationToken).ConfigureAwait(false);
        }
    }

    private IEnumerable<string> ExtractLinks(ChatEntry? entry)
        => MarkupParser.ExtractLinks(entry?.Content ?? "")
            .Where(x => !UrlMapper.IsTrustedGifUrl(x))
            .Take(Constants.Media.LinkPreviewsPerMessageLimit);
}
