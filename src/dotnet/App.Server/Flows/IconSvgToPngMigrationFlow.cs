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
        return rows.Select(x => new UsedMedia(x.Id, x.MediaId, false)).ToList();
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
        return rows.Select(x => new UsedMedia(x.Id, x.MediaId, false)).ToList();
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
                result.Add(new UsedMedia(p.Id, p.MediaId, false));
            if (!p.BackgroundMediaId.IsNullOrEmpty())
                result.Add(new UsedMedia(p.Id, p.BackgroundMediaId, true));
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
        var svgMediaId = MediaId.Parse(item.MediaId);
        var svg = await MediaBackend.GetFull(svgMediaId, cancellationToken).ConfigureAwait(false);
        if (svg is null || !svg.BlobId.EndsWith(".svg"))
            return false;

        // Defensive: refuse to repoint if some attachment table also references this media id.
        // We only rewrite Avatar/Chat/Place references; leaving an attachment dangling would
        // be a real bug. In practice icons and attachments don't share MediaIds.
        if (await IsReferencedOutsideIcons(svgMediaId, cancellationToken).ConfigureAwait(false)) {
            Console.LogWarning($"Skipping {svgMediaId.Value}: referenced outside icon tables");
            return false;
        }

        // 1. Allocate a new MediaId in the same scope and rasterize the SVG into a new PNG blob.
        var pngMediaId = MediaId.New(svgMediaId.Scope);
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
            Metadata = pngMedia.Metadata.Set(ReplacesMediaIdMetadataKey, svgMediaId.Value),
        };
        await Commander.Call(
            new MediaBackend_Change(pngMediaId, null, new Change<MediaFull> { Create = pngMedia }),
            true, cancellationToken).ConfigureAwait(false);

        // 3. Repoint the host entity (Avatar / Chat / Place) at the new PNG MediaId
        //    via the appropriate *Backend_Change command. The original SVG Media row
        //    and SVG blob remain untouched and form a self-contained backup.
        await RepointReference(item, pngMediaId, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private Task RepointReference(UsedMedia item, MediaId pngMediaId, CancellationToken cancellationToken)
        => Phase switch {
            MigrationPhase.Avatars => RepointAvatar(item.EntityId, pngMediaId, cancellationToken),
            MigrationPhase.Chats => RepointChat(item.EntityId, pngMediaId, cancellationToken),
            MigrationPhase.Places => RepointPlace(item.EntityId, item.IsBackground, pngMediaId, cancellationToken),
            _ => Task.CompletedTask,
        };

    private async Task RepointAvatar(string avatarIdValue, MediaId pngMediaId, CancellationToken cancellationToken)
    {
        // Load the row directly rather than via AvatarsBackend.Get; the resolver
        // cache may not see avatars added through paths other than the backend.
        var db = await UsersDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);
        var dbAvatar = await db.Avatars
            .FirstOrDefaultAsync(x => x.Id == avatarIdValue, cancellationToken)
            .ConfigureAwait(false);
        if (dbAvatar is null) {
            Console.LogWarning($"Avatar {avatarIdValue} disappeared before repoint");
            return;
        }
        // AvatarsBackend.OnChange takes a full AvatarFull and overwrites all fields, so
        // we hand it the existing row state with only MediaId swapped.
        var updated = dbAvatar.ToModel() with { MediaId = pngMediaId };
        await Commander.Call(
            new AvatarsBackend_Change((Symbol)avatarIdValue, null, new Change<AvatarFull> { Update = updated }),
            true, cancellationToken).ConfigureAwait(false);
    }

    private Task RepointChat(string chatIdValue, MediaId pngMediaId, CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(chatIdValue);
        return Commander.Call(
            new ChatsBackend_Change(chatId, null, new Change<ChatDiff> {
                Update = new ChatDiff { MediaId = pngMediaId },
            }),
            true, cancellationToken);
    }

    private Task RepointPlace(string placeIdValue, bool isBackground, MediaId pngMediaId, CancellationToken cancellationToken)
    {
        var placeId = PlaceId.Parse(placeIdValue);
        var diff = isBackground
            ? new PlaceDiff { BackgroundMediaId = pngMediaId }
            : new PlaceDiff { MediaId = pngMediaId };
        return Commander.Call(
            new PlacesBackend_Change(placeId, null, new Change<PlaceDiff> { Update = diff }),
            true, cancellationToken);
    }

    private async Task<bool> IsReferencedOutsideIcons(MediaId mediaId, CancellationToken cancellationToken)
    {
        var db = await ChatDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);

        var sid = mediaId.Value;
        return await db.ChatEntryAttachments
            .AnyAsync(x => x.MediaId == sid || x.ThumbnailMediaId == sid, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PngBlobInfo?> ConvertSvgBlobToPng(MediaId mediaId, string svgBlobId, CancellationToken cancellationToken)
    {
        var svgStream = await BlobStorage.Read(svgBlobId, cancellationToken).ConfigureAwait(false);
        if (svgStream == null) {
            Console.LogWarning($"Blob not found for media {mediaId.Value}: {svgBlobId}");
            return null;
        }
        await using var _1 = svgStream.ConfigureAwait(false);
        Console.Log($"ConvertSvgToPng: parsing SVG ({svgStream.Length} bytes)");
        var (pngStream, size) = SvgRasterizer.RasterizeToPng(svgStream, MaxSize);
        await using var _2 = pngStream.ConfigureAwait(false);
        Console.Log($"ConvertSvgToPng: encoded PNG {size.Width}x{size.Height} ({pngStream.Length} bytes)");

        var newBlobId = MediaSaver.GetBlobId(mediaId, ".png");
        await BlobStorage.Write(newBlobId, pngStream, "image/png", cancellationToken).ConfigureAwait(false);
        return new PngBlobInfo(newBlobId, size, pngStream.Length);
    }

    // Nested types

    public enum MigrationPhase
    {
        Avatars = 0,
        Chats = 1,
        Places = 2,
        Done = 3,
    }

    private sealed record UsedMedia(string EntityId, string MediaId, bool IsBackground);

    private sealed record PngBlobInfo(string BlobId, Size Size, long Length);
}
