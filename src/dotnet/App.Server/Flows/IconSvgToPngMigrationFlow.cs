using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualChat.Media.Db;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Svg.Skia;

namespace ActualChat.App.Server.Flows;

/// <summary>
/// Migrates existing SVG icon blobs to PNG.
/// Scans DbAvatar, DbChat, and DbPlace to collect media IDs,
/// then converts any SVG media to PNG via SkiaSharp.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class IconSvgToPngMigrationFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 50;
    private const int MaxSize = 1920;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(2).ToRandom(0.25);

    private DbHub<UsersDbContext> UsersDbHub => field ??= Services.DbHub<UsersDbContext>();
    private DbHub<ChatDbContext> ChatDbHub => field ??= Services.DbHub<ChatDbContext>();
    private DbHub<MediaDbContext> MediaDbHub => field ??= Services.DbHub<MediaDbContext>();
    private IBlobStorage BlobStorage => field ??= Services.BlobStorages()[BlobScope.ContentRecord];

    /// <summary> 0 = Avatars, 1 = Chats, 2 = Places, 3 = Done </summary>
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int Phase { get; set; }

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public string? LastProcessedEntityId { get; set; }

    [DataMember(Order = 2), MemoryPackOrder(2)]
    public long ConvertedCount { get; set; }

    [DataMember(Order = 3), MemoryPackOrder(3)]
    public long SkippedCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        while (Phase < 3) {
            var mediaIds = await GetNextBatch(cancellationToken).ConfigureAwait(false);
            if (mediaIds.Count == 0) {
                // Current phase done, move to next
                Phase++;
                LastProcessedEntityId = null;
                continue;
            }

            await ConvertBatch(mediaIds, cancellationToken).ConfigureAwait(false);

            var phaseName = Phase switch { 0 => "Avatars", 1 => "Chats", 2 => "Places", _ => "?" };
            Console.Log($"Phase {phaseName}: {ConvertedCount} converted, {SkippedCount} skipped");

            if (mediaIds.Count < BatchSize)  {
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

    private async Task<List<(string EntityId, string MediaId)>> GetNextBatch(CancellationToken cancellationToken)
        => Phase switch {
            0 => await GetAvatarBatch(cancellationToken).ConfigureAwait(false),
            1 => await GetChatBatch(cancellationToken).ConfigureAwait(false),
            2 => await GetPlaceBatch(cancellationToken).ConfigureAwait(false),
            _ => [],
        };

    private async Task<List<(string EntityId, string MediaId)>> GetAvatarBatch(CancellationToken cancellationToken)
    {
        var db = await UsersDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);

        var query = db.Avatars
            .Where(x => x.MediaId != "")
            .OrderBy(x => x.Id)
            .AsQueryable();

        if (!LastProcessedEntityId.IsNullOrEmpty())
            query = query.Where(x => string.Compare(x.Id, LastProcessedEntityId) > 0);

        return await query
            .Take(BatchSize)
            .Select(x => ValueTuple.Create(x.Id, x.MediaId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<(string EntityId, string MediaId)>> GetChatBatch(CancellationToken cancellationToken)
    {
        var db = await ChatDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);

        var query = db.Chats
            .Where(x => x.MediaId != "")
            .OrderBy(x => x.Id)
            .AsQueryable();

        if (!LastProcessedEntityId.IsNullOrEmpty())
            query = query.Where(x => string.Compare(x.Id, LastProcessedEntityId) > 0);

        return await query
            .Take(BatchSize)
            .Select(x => ValueTuple.Create(x.Id, x.MediaId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<(string EntityId, string MediaId)>> GetPlaceBatch(CancellationToken cancellationToken)
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
        var result = new List<(string, string)>();
        foreach (var p in places) {
            if (!p.MediaId.IsNullOrEmpty())
                result.Add((p.Id, p.MediaId));
            if (!p.BackgroundMediaId.IsNullOrEmpty())
                result.Add((p.Id, p.BackgroundMediaId));
        }
        return result;
    }

    private async Task ConvertBatch(
        List<(string EntityId, string MediaId)> items,
        CancellationToken cancellationToken)
    {
        var mediaDb = await MediaDbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = mediaDb.ConfigureAwait(false);

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
        var metadata = MetadataSerializer.Read(dbMedia.MetadataJson);
        var contentType = metadata[nameof(Media.Media.ContentType)] as string;
        if (!MediaTypeExt.IsSvg(contentType))
            return false;

        var svgStream = await BlobStorage.Read(dbMedia.BlobId, cancellationToken).ConfigureAwait(false);
        if (svgStream == null) {
            Console.LogWarning($"Blob not found for media {dbMedia.Id}: {dbMedia.BlobId}");
            return false;
        }

        byte[] pngBytes;
        int width, height;
        await using (svgStream.ConfigureAwait(false)) {
            (pngBytes, width, height) = ConvertSvgToPng(svgStream);
        }

        var mediaId = MediaId.Parse(dbMedia.Id);
        var newBlobId = MediaSaver.GetBlobId(mediaId, ".png");
        using var pngStream = new MemoryStream(pngBytes);
        await BlobStorage.Write(newBlobId, pngStream, "image/png", cancellationToken).ConfigureAwait(false);

        var fileName = metadata[nameof(Media.Media.FileName)] as string ?? "";
        metadata = metadata
            .Set(nameof(Media.Media.ContentType), "image/png")
            .Set(nameof(Media.Media.FileName), Path.ChangeExtension(fileName, ".png"))
            .Set(nameof(Media.Media.Width), width)
            .Set(nameof(Media.Media.Height), height)
            .Set(nameof(Media.Media.Length), (long)pngBytes.Length);

        dbMedia.BlobId = newBlobId;
        dbMedia.MetadataJson = MetadataSerializer.Write(metadata);
        return true;
    }

    private static (byte[] PngBytes, int Width, int Height) ConvertSvgToPng(Stream svgStream)
    {
        using var svg = SKSvg.CreateFromStream(svgStream);
        var picture = svg.Picture
            ?? throw StandardError.Internal("Failed to parse SVG file.");

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw StandardError.Internal("SVG has invalid dimensions.");

        var (targetWidth, targetHeight) = ComputeTargetSize((int)bounds.Width, (int)bounds.Height);
        var scaleX = targetWidth / bounds.Width;
        var scaleY = targetHeight / bounds.Height;

        var imageInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scaleX, scaleY);
        canvas.DrawPicture(picture);

        using var pixmap = surface.PeekPixels();
        using var data = pixmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw StandardError.Internal("Failed to encode SVG as PNG.");
        return (data.ToArray(), targetWidth, targetHeight);
    }

    private static (int Width, int Height) ComputeTargetSize(int width, int height)
    {
        if (width <= MaxSize && height <= MaxSize)
            return (width, height);

        var scale = Math.Min((float)MaxSize / width, (float)MaxSize / height);
        return ((int)(width * scale), (int)(height * scale));
    }
}
