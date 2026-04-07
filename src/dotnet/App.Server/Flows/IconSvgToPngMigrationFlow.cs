using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualChat.Uploads;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.App.Server.Flows;

/// <summary>
/// Migrates existing SVG icon blobs to PNG.
/// Walks <see cref="DbAvatar"/>, <see cref="DbChat"/>, and <see cref="DbPlace"/>
/// to collect media IDs that are actually used as icons, then converts any
/// of those that are SVG to PNG via SkiaSharp.
/// Scanning the referencing tables (rather than the Media table directly)
/// is the only way to leave chat-entry attachment SVGs untouched, because
/// old media records have <see cref="MediaKind.Unknown"/>.
/// </summary>
/// <remarks>
/// Conversion allocates a brand new <see cref="MediaId"/> for the PNG and
/// repoints the host entity (avatar/chat/place) at it via the appropriate
/// backend command. The original SVG <see cref="MediaFull"/> row and SVG
/// blob are left fully untouched, forming a complete self-contained backup.
/// The new PNG row carries <see cref="ReplacesMediaIdMetadataKey"/> in its metadata
/// pointing back at the original SVG MediaId — this is backup-only data,
/// not read by application code, but a future restore/cleanup tool can
/// consume it.
/// </remarks>
[Flow(DataVersion = 2, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class IconSvgToPngMigrationFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 50;
    private const int MaxSize = Constants.Attachments.MaxIconSize;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(2).ToRandom(0.25);

    // Metadata key written on the *new* PNG Media row pointing back at the original
    // SVG MediaId. Backup data only — not read by application code. A future
    // restore/rerun/cleanup tool would consume it.
    private const string ReplacesMediaIdMetadataKey = "ReplacesMediaId";

    private DbHub<UsersDbContext> UsersDbHub => field ??= Services.DbHub<UsersDbContext>();
    private DbHub<ChatDbContext> ChatDbHub => field ??= Services.DbHub<ChatDbContext>();
    private IBlobStorage BlobStorage => field ??= Services.BlobStorages()[BlobScope.ContentRecord];
    private SvgRasterizer SvgRasterizer => field ??= Services.GetRequiredService<SvgRasterizer>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public MigrationPhase Phase { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public string? LastProcessedEntityId { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public long ConvertedCount { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public long SkippedCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        while (Phase < MigrationPhase.Done) {
            var items = await GetNextBatch(cancellationToken).ConfigureAwait(false);
            if (items.Count == 0) {
                // Current phase done, move to next
                Phase++;
                LastProcessedEntityId = null;
                continue;
            }

            await ConvertBatch(items, cancellationToken).ConfigureAwait(false);

            Console.Log($"Phase {Phase}: {ConvertedCount} converted, {SkippedCount} skipped");

            if (items.Count < BatchSize) {
                Phase++;
                LastProcessedEntityId = null;
                continue;
            }

            Runtime.StageResumeIn(BatchDelay.Next());
            return;
        }

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
            .Where(x => x.MediaId != "")
            .OrderBy(x => x.Id)
            .AsQueryable();
        if (!LastProcessedEntityId.IsNullOrEmpty())
            query = query.Where(x => string.Compare(x.Id, LastProcessedEntityId) > 0);

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
            .Where(x => x.MediaId != "")
            .OrderBy(x => x.Id)
            .AsQueryable();
        if (!LastProcessedEntityId.IsNullOrEmpty())
            query = query.Where(x => string.Compare(x.Id, LastProcessedEntityId) > 0);

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

        var query = db.Places
            .Where(x => x.MediaId != "" || x.BackgroundMediaId != "")
            .OrderBy(x => x.Id)
            .AsQueryable();
        if (!LastProcessedEntityId.IsNullOrEmpty())
            query = query.Where(x => string.Compare(x.Id, LastProcessedEntityId) > 0);

        var places = await query
            .Take(BatchSize)
            .Select(x => new { x.Id, x.MediaId, x.BackgroundMediaId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Flatten: a place can have both MediaId and BackgroundMediaId
        var result = new List<UsedMedia>();
        foreach (var p in places) {
            if (!p.MediaId.IsNullOrEmpty())
                result.Add(new UsedMedia(p.Id, MediaId.Parse(p.MediaId), false));
            if (!p.BackgroundMediaId.IsNullOrEmpty())
                result.Add(new UsedMedia(p.Id, MediaId.Parse(p.BackgroundMediaId), true));
        }
        return result;
    }

    // Private methods - conversion

    private async Task ConvertBatch(List<UsedMedia> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
            try {
                var converted = await ProcessOne(item, cancellationToken).ConfigureAwait(false);
                if (converted)
                    ConvertedCount++;
                else
                    SkippedCount++;
            }
            catch (Exception e) {
                Console.LogError($"Failed to convert media {item.MediaId}: {e.Message}", e);
                SkippedCount++;
            }

        LastProcessedEntityId = items[^1].EntityId;
    }

    private async Task<bool> ProcessOne(UsedMedia item, CancellationToken cancellationToken)
    {
        var svg = await MediaBackend.GetFull(item.MediaId, cancellationToken).ConfigureAwait(false);
        if (svg is null || !svg.BlobId.EndsWith(".svg"))
            return false;

        // 1. Allocate a new MediaId in the same scope and rasterize the SVG into a new PNG blob.
        var pngMediaId = MediaId.New(svg.Id.Scope);
        var pngBlob = await ConvertSvgBlobToPng(pngMediaId, svg.BlobId, cancellationToken).ConfigureAwait(false);
        if (pngBlob is null)
            return false;

        // 2. Create the new PNG Media row carrying ReplacesMediaId provenance.
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

        // 3. Repoint the host entity (Avatar / Chat / Place) at the new PNG MediaId
        //    via the appropriate *Backend_Change command. The original SVG Media row
        //    and SVG blob remain untouched and form a self-contained backup.
        // NOTE: A future cleanup flow can drop SVG rows that are no longer referenced
        //    anywhere (icons or chat-entry attachments) using the ReplacesMediaId
        //    metadata as the starting set.
        await RepointReference(item with { MediaId = pngMediaId }, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private Task RepointReference(UsedMedia item, CancellationToken cancellationToken)
        => Phase switch {
            MigrationPhase.Avatars => RepointAvatar(item, cancellationToken),
            MigrationPhase.Chats => RepointChat(item, cancellationToken),
            MigrationPhase.Places => RepointPlace(item, cancellationToken),
            _ => Task.CompletedTask,
        };

    private async Task RepointAvatar(UsedMedia item, CancellationToken cancellationToken)
    {
        // Load the row directly rather than via AvatarsBackend.Get; the resolver
        // cache may not see avatars added through paths other than the backend.
        var db = await UsersDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);
        var dbAvatar = await db.Avatars
            .FirstOrDefaultAsync(x => x.Id == item.EntityId, cancellationToken)
            .ConfigureAwait(false);
        if (dbAvatar is null) {
            Console.LogWarning($"Avatar {item.EntityId} disappeared before repoint");
            return;
        }
        var updated = dbAvatar.ToModel() with { MediaId = item.MediaId };
        await Commander.Call(
            new AvatarsBackend_Change((Symbol)item.EntityId, null, Change.Update(updated)),
            true, cancellationToken).ConfigureAwait(false);
    }

    private Task RepointChat(UsedMedia item, CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(item.EntityId);
        return Commander.Call(
            new ChatsBackend_Change(chatId, null, Change.Update(new ChatDiff { MediaId = item.MediaId })),
            true, cancellationToken);
    }

    private Task RepointPlace(UsedMedia item, CancellationToken cancellationToken)
    {
        var placeId = PlaceId.Parse(item.EntityId);
        var diff = item.IsBackground
            ? new PlaceDiff { BackgroundMediaId = item.MediaId }
            : new PlaceDiff { MediaId = item.MediaId };
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

    private sealed record PngBlobInfo(string BlobId, Size Size, long Length);
}
