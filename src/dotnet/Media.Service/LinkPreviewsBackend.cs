using ActualChat.Chat;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Media.Db;
using ActualChat.Media.Flows;
using ActualChat.Media.Module;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

public class LinkPreviewsBackend(IServiceProvider services)
    : DbServiceBase<MediaDbContext>(services), ILinkPreviewsBackend
{
    private IFlows Flows => field ??= Services.GetRequiredService<IFlows>();
    private MediaSettings Settings => field ??= Services.GetRequiredService<MediaSettings>();
    private IMarkupParser MarkupParser => field ??= Services.GetRequiredService<IMarkupParser>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private IDbEntityResolver<string, DbLinkPreview> EntityResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbLinkPreview>>();
    private Moment SystemNow => Clocks.SystemClock.Now;

    // [ComputeMethod]
    public virtual async Task<LinkPreview?> Get(
        Symbol id,
        bool mustScheduleRefreshIfRequired,
        CancellationToken cancellationToken)
    {
        var dbLinkPreview = await EntityResolver.Get(id, cancellationToken).ConfigureAwait(false);
        var linkPreview = dbLinkPreview?.ToModel();

        await ScheduleRefreshIfRequired().ConfigureAwait(false);
        if (linkPreview == null || linkPreview.PreviewMediaId == null)
            return linkPreview;

        return linkPreview with {
            PreviewMedia = await MediaBackend.Get(linkPreview.PreviewMediaId, cancellationToken).ConfigureAwait(false),
        };

        Task ScheduleRefreshIfRequired()
            => mustScheduleRefreshIfRequired && linkPreview != null && NeedsUpdate(linkPreview.ModifiedAt)
                ? Flows.LegacyReset<LinkPreviewFlow>(LinkPreviewFlow.GetArguments(linkPreview.Url),
                    Settings.LinkPreviewUpdatePeriod,
                    "Get link preview",
                    cancellationToken)
                : Task.CompletedTask;
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
    public virtual Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind, oldEntry) = eventCommand;
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        return ScheduleNewLinksCrawling();

        async Task ScheduleNewLinksCrawling() {
            if(changeKind is ChangeKind.Remove)
                return;

            var links = ExtractLinks(entry);
            var oldLinks = ExtractLinks(oldEntry);
            foreach (var link in links.Take(Constants.Media.LinkPreviewsPerMessageLimit).Except(oldLinks, StringComparer.Ordinal))
                await Flows.Get<LinkPreviewFlow>(LinkPreviewFlow.GetArguments(link), cancellationToken).ConfigureAwait(false);
        }
    }

    private IEnumerable<string> ExtractLinks(ChatEntry? entry)
        => MarkupParser.ExtractLinks(entry?.Content ?? "", Constants.Media.LinkPreviewsPerMessageLimit);

    // Private methods

    private bool NeedsUpdate(Moment modifiedAt)
        => modifiedAt + Settings.LinkPreviewUpdatePeriod < SystemNow;
}
