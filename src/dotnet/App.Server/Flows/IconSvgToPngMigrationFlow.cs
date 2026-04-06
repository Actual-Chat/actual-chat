using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualChat.Media.Db;
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
/// Scanning the referencing tables (rather than <see cref="DbMedia"/> directly)
/// is the only way to leave chat-entry attachment SVGs untouched, because
/// old <see cref="DbMedia"/> records have <see cref="MediaKind.Unknown"/>.
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
    private DbHub<MediaDbContext> MediaDbHub => field ??= Services.DbHub<MediaDbContext>();
    private IBlobStorage BlobStorage => field ??= Services.BlobStorages()[BlobScope.ContentRecord];
    private SvgRasterizer SvgRasterizer => field ??= Services.GetRequiredService<SvgRasterizer>();

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
        var mediaDb = await MediaDbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = mediaDb.ConfigureAwait(false);

        var mediaIds = items.Select(x => x.MediaId).Distinct().ToList();
        var dbMediaRecords = await mediaDb.Media
            .Where(x => mediaIds.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var dbMedia in dbMediaRecords) {
            try {
                var converted = await ProcessOne(dbMedia, cancellationToken).ConfigureAwait(false);
                if (converted)
                    ConvertedCount++;
                else
                    SkippedCount++;
            }
            catch (Exception e) {
                Console.LogError($"Failed to convert media {dbMedia.Id}: {e.Message}", e);
                SkippedCount++;
            }
        }

        await mediaDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LastProcessedEntityId = items[^1].EntityId;
    }

    private async Task<bool> ProcessOne(DbMedia dbMedia, CancellationToken cancellationToken)
    {
        if (!dbMedia.BlobId.EndsWith(".svg"))
            return false;

        var pngBlob = await ConvertSvgBlobToPng(dbMedia, cancellationToken).ConfigureAwait(false);
        if (pngBlob == null)
            return false;

        var metadata = MetadataSerializer.Read(dbMedia.MetadataJson);
        var fileName = metadata[nameof(Media.Media.FileName)] as string ?? "";
        metadata = metadata
            .Set(nameof(Media.Media.ContentType), "image/png")
            .Set(nameof(Media.Media.FileName), Path.ChangeExtension(fileName, ".png"))
            .Set(nameof(Media.Media.Width), pngBlob.Size.Width)
            .Set(nameof(Media.Media.Height), pngBlob.Size.Height)
            .Set(nameof(Media.Media.Length), pngBlob.Length);

        dbMedia.BlobId = pngBlob.BlobId;
        dbMedia.MetadataJson = MetadataSerializer.Write(metadata);
        return true;
    }

    private async Task<PngBlobInfo?> ConvertSvgBlobToPng(DbMedia dbMedia, CancellationToken cancellationToken)
    {
        var svgStream = await BlobStorage.Read(dbMedia.BlobId, cancellationToken).ConfigureAwait(false);
        if (svgStream == null) {
            Console.LogWarning($"Blob not found for media {dbMedia.Id}: {dbMedia.BlobId}");
            return null;
        }
        await using var _1 = svgStream.ConfigureAwait(false);
        Console.Log($"ConvertSvgToPng: parsing SVG ({svgStream.Length} bytes)");
        var (pngStream, size) = SvgRasterizer.RasterizeToPng(svgStream, MaxSize);
        await using var _2 = pngStream.ConfigureAwait(false);
        Console.Log($"ConvertSvgToPng: encoded PNG {size.Width}x{size.Height} ({pngStream.Length} bytes)");

        var mediaId = MediaId.Parse(dbMedia.Id);
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
