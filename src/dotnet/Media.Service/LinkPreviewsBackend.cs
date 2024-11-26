using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.Media.Db;
using ActualChat.Media.Flows;
using ActualChat.Media.Module;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Media;

public class LinkPreviewsBackend(IServiceProvider services)
    : DbServiceBase<MediaDbContext>(services), ILinkPreviewsBackend
{
    [field: AllowNull, MaybeNull]
    private IFlows Flows => field ??= Services.GetRequiredService<IFlows>();
    [field: AllowNull, MaybeNull]
    private MediaSettings Settings => field ??= Services.GetRequiredService<MediaSettings>();
    [field: AllowNull, MaybeNull]
    private IMarkupParser MarkupParser => field ??= Services.GetRequiredService<IMarkupParser>();
    [field: AllowNull, MaybeNull]
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();

    private Moment SystemNow => Clocks.SystemClock.Now;

    // [ComputeMethod]
    public virtual async Task<LinkPreview?> Get(
        Symbol id,
        bool mustScheduleRefreshIfRequired,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbLinkPreview = await dbContext.LinkPreviews.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        var linkPreview = dbLinkPreview?.ToModel();

        await ScheduleRefreshIfRequired();
        if (linkPreview?.PreviewMediaId.IsNone != false)
            return linkPreview;

        return linkPreview with {
            PreviewMedia = await MediaBackend.Get(linkPreview.PreviewMediaId, cancellationToken).ConfigureAwait(false),
        };

        Task ScheduleRefreshIfRequired()
            => mustScheduleRefreshIfRequired && linkPreview != null && NeedsUpdate(linkPreview.ModifiedAt)
                ? Flows.GetAndResume<LinkPreviewFlow>(linkPreview.Url,
                    Settings.LinkPreviewUpdatePeriod,
                    "Get link preview",
                    null,
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
        var dbLinkPreview = await dbContext.LinkPreviews.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        if (change.IsCreate(out var linkPreview)) {
            if (dbLinkPreview != null)
                return dbLinkPreview.ToModel();

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
            linkPreview = linkPreview with {
                CreatedAt = dbLinkPreview.CreatedAt,
            };
            dbLinkPreview = new DbLinkPreview(linkPreview) {
                Version = VersionGenerator.NextVersion(dbLinkPreview.Version),
                ModifiedAt = SystemNow,
            };
            dbContext.Add(dbLinkPreview);
        }
        else
            throw StandardError.NotSupported("Link previews cannot be removed.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbLinkPreview.ToModel();
    }

    // Event handlers

    // [EventHandler]
    public virtual Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (entry, _, changeKind, oldEntry) = eventCommand;
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        return ScheduleNewLinksCrawling();

        async Task ScheduleNewLinksCrawling()
        {
            if(changeKind is ChangeKind.Remove)
                return;

            var links = ExtractLinks(entry);
            var oldLinks = ExtractLinks(oldEntry);
            foreach (var link in links.Take(Constants.Media.LinkPreviewsPerMessageLimit).Except(oldLinks))
                await Flows.GetOrStart<LinkPreviewFlow>(link.ToBase64(), cancellationToken);
        }
    }

    private IEnumerable<string> ExtractLinks(ChatEntry? entry)
        => MarkupParser.ExtractLinks(entry?.Content ?? "", Constants.Media.LinkPreviewsPerMessageLimit);

    // Private methods

    private bool NeedsUpdate(Moment modifiedAt)
        => modifiedAt + Settings.LinkPreviewUpdatePeriod < SystemNow;
}
