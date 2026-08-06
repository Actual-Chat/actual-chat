using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualChat.Uploads;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.App.Server.Flows;

/// <summary>
/// Migrates existing SVG icon blobs to PNG by walking <see cref="DbAvatar"/>,
/// <see cref="DbChat"/>, and <see cref="DbPlace"/> and converting the SVGs they reference.
/// </summary>
/// <remarks>
/// Scanning referencing tables (rather than Media directly) is the only way to leave
/// chat-entry attachment SVGs untouched — old media rows have <see cref="MediaKind.Unknown"/>.
///
/// Each conversion allocates a new <see cref="MediaId"/>, writes a PNG
/// <see cref="MediaFull"/> tagged with <see cref="ReplacesMediaIdMetadataKey"/>, and
/// repoints the host entity. The original SVG row and blob are preserved as a
/// self-contained backup; a future cleanup tool can use the metadata key to find them.
///
/// <see cref="SystemIconsPrefix"/> rows are excluded at the SQL level — their IDs are
/// hard-coded in <c>MediaDbInitializer</c>/<c>ChatsBackend</c>/<see cref="Constants"/>,
/// and <c>MediaDbInitializer</c> already upgrades them to PNG in place on startup.
/// </remarks>
[Flow(DataVersion = 2, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class IconSvgToPngMigrationFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 50;
    private const int MaxSize = Constants.Attachments.MaxIconSize;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(2).ToRandom(0.25);

    // Seeded system icons (e.g. "system-icons:notes"). Filtered out of every batch:
    // MediaDbInitializer already upgrades them in place on startup.
    private const string SystemIconsPrefix = "system-icons:";

    // Metadata key on the new PNG row pointing back at the original SVG MediaId.
    // Backup-only — not read by application code.
    private const string ReplacesMediaIdMetadataKey = "ReplacesMediaId";

    private DbHub<UsersDbContext> UsersDbHub => field ??= Services.DbHub<UsersDbContext>();
    private DbHub<ChatDbContext> ChatDbHub => field ??= Services.DbHub<ChatDbContext>();
    private IBlobStorage BlobStorage => field ??= Services.BlobStorages()[BlobScope.ContentRecord];
    private SvgRasterizer SvgRasterizer => field ??= Services.GetRequiredService<SvgRasterizer>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public MigrationPhase Phase { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public string? LastProcessedEntityId { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public long ConvertedCount { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public long SkippedCount { get; set; }
    // Unexpected ProcessOne exceptions. Non-zero at completion needs dev attention.
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public long FailedCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        while (Phase < MigrationPhase.Done) {
            var items = await GetNextBatch(cancellationToken).ConfigureAwait(false);
            if (items.Count == 0) {
                Phase++;
                LastProcessedEntityId = null;
                continue;
            }

            var convertedInBatch = await ConvertBatch(items, cancellationToken).ConfigureAwait(false);

            Console.Log($"Phase {Phase}: {ConvertedCount} converted, {SkippedCount} skipped, {FailedCount} failed");

            if (items.Count < BatchSize) {
                Phase++;
                LastProcessedEntityId = null;
                continue;
            }
            if (convertedInBatch == 0)
                continue;

            Runtime.StageResumeIn(BatchDelay.Next());
            return;
        }

        if (FailedCount > 0)
            Console.LogError(
                $"Completed with failures: {ConvertedCount} converted, {SkippedCount} skipped, {FailedCount} FAILED — "
                + "review error logs for 'Failed to convert media' entries and take action on affected entities.");
        else
            Console.Log($"Completed: {ConvertedCount} converted, {SkippedCount} skipped");
        SetResult((Hub.Clocks.SystemClock.Now, ConvertedCount));
    }

    // Private methods - batch fetching

    private async Task<List<UsedMedia>> GetNextBatch(CancellationToken cancellationToken)
        => Phase switch {
            MigrationPhase.Avatars => await GetAvatarBatch(cancellationToken).ConfigureAwait(false),
            MigrationPhase.Chats => await GetChatBatch(cancellationToken).ConfigureAwait(false),
            MigrationPhase.Places => await GetPlaceBatch(cancellationToken).ConfigureAwait(false),
            _ => [],
        };

    private async Task<List<UsedMedia>> GetAvatarBatch(CancellationToken cancellationToken)
    {
        var db = await UsersDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);

        var query = db.Avatars
            .Where(x => x.MediaId != "" && !x.MediaId.StartsWith(SystemIconsPrefix))
            .OrderBy(x => x.Id)
            .AsQueryable();
        if (!LastProcessedEntityId.IsNullOrEmpty())
            query = query.Where(x => x.Id.CompareTo(LastProcessedEntityId) > 0);

        var rows = await query
            .Take(BatchSize)
            .Select(x => new { x.Id, x.MediaId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ConvertAll(x => new UsedMedia(x.Id, MediaId.Parse(x.MediaId), false));
    }

    private async Task<List<UsedMedia>> GetChatBatch(CancellationToken cancellationToken)
    {
        var db = await ChatDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);

        var query = db.Chats
            .Where(x => x.MediaId != "" && !x.MediaId.StartsWith(SystemIconsPrefix))
            .OrderBy(x => x.Id)
            .AsQueryable();
        if (!LastProcessedEntityId.IsNullOrEmpty())
            query = query.Where(x => x.Id.CompareTo(LastProcessedEntityId) > 0);

        var rows = await query
            .Take(BatchSize)
            .Select(x => new { x.Id, x.MediaId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ConvertAll(x => new UsedMedia(x.Id, MediaId.Parse(x.MediaId), false));
    }

    private async Task<List<UsedMedia>> GetPlaceBatch(CancellationToken cancellationToken)
    {
        var db = await ChatDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);

        // Load places with at least one non-system-icons slot; the flatten below
        // drops any individual system-icons values.
        var query = db.Places
            .Where(x =>
                (x.MediaId != "" && !x.MediaId.StartsWith(SystemIconsPrefix))
                || (x.BackgroundMediaId != "" && !x.BackgroundMediaId.StartsWith(SystemIconsPrefix)))
            .OrderBy(x => x.Id)
            .AsQueryable();
        if (!LastProcessedEntityId.IsNullOrEmpty())
            query = query.Where(x => x.Id.CompareTo(LastProcessedEntityId) > 0);

        var places = await query
            .Take(BatchSize)
            .Select(x => new { x.Id, x.MediaId, x.BackgroundMediaId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Flatten both slots; only non-system-icons values become candidates.
        var result = new List<UsedMedia>();
        foreach (var p in places) {
            if (!p.MediaId.IsNullOrEmpty() && !p.MediaId.StartsWith(SystemIconsPrefix, StringComparison.Ordinal))
                result.Add(new UsedMedia(p.Id, MediaId.Parse(p.MediaId), false));
            if (!p.BackgroundMediaId.IsNullOrEmpty() && !p.BackgroundMediaId.StartsWith(SystemIconsPrefix, StringComparison.Ordinal))
                result.Add(new UsedMedia(p.Id, MediaId.Parse(p.BackgroundMediaId), true));
        }
        return result;
    }

    // Private methods - conversion

    private async Task<long> ConvertBatch(List<UsedMedia> items, CancellationToken cancellationToken)
    {
        var convertedBefore = ConvertedCount;
        foreach (var item in items)
            try {
                var converted = await ProcessOne(item, cancellationToken).ConfigureAwait(false);
                if (converted)
                    ConvertedCount++;
                else
                    SkippedCount++;
            }
            catch (Exception e) {
                // Intentional no-ops (missing blob, not-an-SVG, concurrent change) return
                // normally; anything reaching here is unexpected and flagged in the summary.
                Console.LogError($"Failed to convert media {item.MediaId}: {e.Message}", e);
                FailedCount++;
            }

        LastProcessedEntityId = items[^1].EntityId;
        return ConvertedCount - convertedBefore;
    }

    private async Task<bool> ProcessOne(UsedMedia item, CancellationToken cancellationToken)
    {
        var svg = await MediaBackend.GetFull(item.MediaId, cancellationToken).ConfigureAwait(false);
        if (svg is null || !svg.BlobId.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return false;

        // 1. Allocate a new MediaId in the same scope; rasterize SVG to a new PNG blob.
        var pngMediaId = MediaId.New(svg.Id.Scope);
        var pngBlob = await ConvertSvgBlobToPng(pngMediaId, svg.BlobId, cancellationToken).ConfigureAwait(false);
        if (pngBlob is null)
            return false;

        // 2. Create the PNG Media row with ReplacesMediaId provenance.
        var pngMedia = new MediaFull(pngMediaId) {
            Kind = svg.Kind,
            BlobId = pngBlob.BlobId,
            ContentType = "image/png",
            FileName = Path.ChangeExtension(svg.FileName, ".png"),
            Width = pngBlob.Size.Width,
            Height = pngBlob.Size.Height,
            Length = pngBlob.Length,
            UserId = svg.UserId,
        };
        pngMedia = pngMedia with {
            Metadata = pngMedia.Metadata.Set(ReplacesMediaIdMetadataKey, svg.Id.Value),
        };
        await Commander.Call(
            new MediaBackend_Change(pngMediaId, null, Change.Create(pngMedia)),
            true, cancellationToken).ConfigureAwait(false);

        // 3. Repoint the host entity at the new PNG. The original SVG row and blob
        //    remain as a self-contained backup; a future cleanup flow can use
        //    ReplacesMediaId to find orphaned SVG rows.
        await Repoint(item, pngMediaId, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private Task Repoint(UsedMedia item, MediaId newMediaId, CancellationToken cancellationToken)
        => Phase switch {
            MigrationPhase.Avatars => RepointAvatar(item, newMediaId, cancellationToken),
            MigrationPhase.Chats => RepointChat(item, newMediaId, cancellationToken),
            MigrationPhase.Places => RepointPlace(item, newMediaId, cancellationToken),
            _ => Task.CompletedTask,
        };

    // Avatar repoint uses AvatarDiff to update only the MediaId field.
    // This avoids version conflicts entirely — no retry logic needed.
    // If the MediaId was concurrently changed (e.g. user uploaded a new picture), we respect it.
    private async Task RepointAvatar(UsedMedia item, MediaId newMediaId, CancellationToken cancellationToken)
    {
        // Read directly — AvatarsBackend's resolver cache may miss avatars
        // created via non-backend paths.
        var db = await UsersDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);
        var dbAvatar = await db.Avatars
            .FirstOrDefaultAsync(x => x.Id == item.EntityId, cancellationToken)
            .ConfigureAwait(false);
        if (dbAvatar is null) {
            Console.Log($"Avatar {item.EntityId} disappeared before repoint; repoint skipped");
            return;
        }
        // Concurrent writer already repointed the avatar (e.g. user uploaded a new picture) — respect it.
        if (dbAvatar.MediaId != item.MediaId.Value) {
            Console.Log($"Avatar {item.EntityId} was concurrently changed to '{dbAvatar.MediaId}'; repoint skipped");
            return;
        }
        // Diff-based update: only changes MediaId, no version conflicts
        await Commander.Call(
            new AvatarsBackend_Change(item.EntityId, null,
                Change.Update(new AvatarDiff {
                    MediaId = Option.Some<MediaId?>(newMediaId),
                })),
            true, cancellationToken).ConfigureAwait(false);
    }

    private Task RepointChat(UsedMedia item, MediaId newMediaId, CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(item.EntityId);
        return Commander.Call(
            new ChatsBackend_Change(chatId, null, Change.Update(new ChatDiff { MediaId = newMediaId })),
            true, cancellationToken);
    }

    private Task RepointPlace(UsedMedia item, MediaId newMediaId, CancellationToken cancellationToken)
    {
        var placeId = PlaceId.Parse(item.EntityId);
        var diff = item.IsBackground
            ? new PlaceDiff { BackgroundMediaId = newMediaId }
            : new PlaceDiff { MediaId = newMediaId };
        return Commander.Call(
            new PlacesBackend_Change(placeId, null, Change.Update(diff)),
            true, cancellationToken);
    }

    private async Task<PngBlobInfo?> ConvertSvgBlobToPng(MediaId pngMediaId, string svgBlobId, CancellationToken cancellationToken)
    {
        var svgStream = await BlobStorage.Read(svgBlobId, cancellationToken).ConfigureAwait(false);
        if (svgStream == null) {
            Console.LogWarning($"Blob not found for media {pngMediaId.Value}: {svgBlobId}");
            return null;
        }
        await using var _1 = svgStream.ConfigureAwait(false);
        Console.Log($"ConvertSvgToPng: parsing SVG ({svgStream.Length} bytes)");
        var (pngStream, size) = SvgRasterizer.RasterizeToPng(svgStream, MaxSize);
        await using var _2 = pngStream.ConfigureAwait(false);
        Console.Log($"ConvertSvgToPng: encoded PNG {size.Width}x{size.Height} ({pngStream.Length} bytes)");

        var pngBlobId = MediaSaver.GetBlobId(pngMediaId, ".png");
        await BlobStorage.Write(pngBlobId, pngStream, "image/png", cancellationToken).ConfigureAwait(false);
        return new PngBlobInfo(pngBlobId, size, pngStream.Length);
    }

    // Nested types

    public enum MigrationPhase
    {
        Avatars = 0,
        Chats = 1,
        Places = 2,
        Done = 3,
    }

    private sealed record UsedMedia(string EntityId, MediaId MediaId, bool IsBackground);

    private sealed record PngBlobInfo(string BlobId, Size2D Size, long Length);
}
