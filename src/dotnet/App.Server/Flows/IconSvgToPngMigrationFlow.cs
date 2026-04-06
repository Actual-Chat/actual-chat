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
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class IconSvgToPngMigrationFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 50;
    private const int MaxSize = Constants.Attachments.MaxIconSize;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(2).ToRandom(0.25);

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
            var mediaIds = await GetNextBatch(cancellationToken).ConfigureAwait(false);
            if (mediaIds.Count == 0) {
                // Current phase done, move to next
                Phase++;
                LastProcessedEntityId = null;
                continue;
            }

            await ConvertBatch(mediaIds, cancellationToken).ConfigureAwait(false);

            Console.Log($"Phase {Phase}: {ConvertedCount} converted, {SkippedCount} skipped");

            if (mediaIds.Count < BatchSize) {
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

    // Private methods

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
        return rows.Select(x => new UsedMedia(x.Id, x.MediaId)).ToList();
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
        return rows.Select(x => new UsedMedia(x.Id, x.MediaId)).ToList();
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
                result.Add(new UsedMedia(p.Id, p.MediaId));
            if (!p.BackgroundMediaId.IsNullOrEmpty())
                result.Add(new UsedMedia(p.Id, p.BackgroundMediaId));
        }
        return result;
    }

    private async Task ConvertBatch(List<UsedMedia> items, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.Where(item => seen.Add(item.MediaId)))
            try {
                var converted = await ProcessOne(MediaId.Parse(item.MediaId), cancellationToken).ConfigureAwait(false);
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

    private async Task<bool> ProcessOne(MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is null || !media.BlobId.EndsWith(".svg"))
            return false;

        var pngBlob = await ConvertSvgBlobToPng(mediaId, media.BlobId, cancellationToken).ConfigureAwait(false);
        if (pngBlob == null)
            return false;

        var updated = media with {
            BlobId = pngBlob.BlobId,
            ContentType = "image/png",
            FileName = Path.ChangeExtension(media.FileName, ".png"),
            Width = pngBlob.Size.Width,
            Height = pngBlob.Size.Height,
            Length = pngBlob.Length,
        };
        var changeCommand = new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Update = updated });
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        return true;
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

    private sealed record UsedMedia(string EntityId, string MediaId);

    private sealed record PngBlobInfo(string BlobId, Size Size, long Length);
}
