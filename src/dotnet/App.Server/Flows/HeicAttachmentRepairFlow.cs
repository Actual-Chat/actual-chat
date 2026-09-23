using System.Net.Mime;
using ActualChat.Chat.Flows;
using ActualChat.Flows;
using ActualChat.Media.Db;
using ActualChat.Uploads;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.App.Server.Flows;

// DataVersion 2: uploads typed application/octet-stream by the client never reached the HEIF sizing,
// so rows kept appearing after the first pass

/// <summary>
/// Turns HEIC/HEIF attachments that were stored as <c>application/octet-stream</c> - before
/// <see cref="HeifReader"/>, the server couldn't size them - back into images, and re-indexes the Media tab
/// of every chat they're in. Blobs stay untouched: those files were downloadable as-is all along.
/// </summary>
[Flow(DataVersion = 2, DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class HeicAttachmentRepairFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 50;
    private const long MaxReadableLength = 64 * 1024 * 1024;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(1).ToRandom(0.25);

    private DbHub<MediaDbContext> MediaDbHub => field ??= Services.DbHub<MediaDbContext>();
    private IBlobStorage BlobStorage => field ??= Services.BlobStorages()[BlobScope.ContentRecord];
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), Key(0)]
    public string? LastProcessedMediaId { get; set; }
    [DataMember(Order = 1), Key(1)]
    public long RepairedCount { get; set; }
    [DataMember(Order = 2), Key(2)]
    public long SkippedCount { get; set; }
    [DataMember(Order = 3), Key(3)]
    public long FailedCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var mediaIds = await GetNextBatch(cancellationToken).ConfigureAwait(false);
        if (mediaIds.Count == 0) {
            Console.Log($"Completed: {RepairedCount} repaired, {SkippedCount} skipped, {FailedCount} failed");
            SetResult((Hub.Clocks.SystemClock.Now, RepairedCount));
            return;
        }

        var chatIds = new HashSet<ChatId>();
        foreach (var mediaId in mediaIds) {
            try {
                if (await Repair(mediaId, cancellationToken).ConfigureAwait(false) is { } chatId) {
                    RepairedCount++;
                    chatIds.Add(chatId);
                }
                else
                    SkippedCount++;
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                FailedCount++;
                Runtime.Log.LogError(e, "Failed to repair HEIC media {MediaId}", mediaId);
            }
            LastProcessedMediaId = mediaId;
        }
        // The Media tab index is denormalized and keyed by entry version, which this doesn't bump
        foreach (var chatId in chatIds)
            await Hub.NewResumeEvent<ChatMediaIndexingFlow>(chatId.Value)
                .WithReset()
                .Schedule(cancellationToken)
                .ConfigureAwait(false);

        Console.Log($"{RepairedCount} repaired, {SkippedCount} skipped, {FailedCount} failed");
        Runtime.StageResumeIn(BatchDelay.Next());
    }

    // Private methods

    private async Task<List<string>> GetNextBatch(CancellationToken cancellationToken)
    {
        var db = await MediaDbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = db.ConfigureAwait(false);

        // ContentType lives in MetadataJson, so this is a scan - fine for a one-off over the Media table
        var query = db.Media.Where(x =>
            (x.BlobId.ToLower().EndsWith(".heic") || x.BlobId.ToLower().EndsWith(".heif"))
            && x.MetadataJson.Contains(MediaTypeNames.Application.Octet));
        if (!LastProcessedMediaId.IsNullOrEmpty())
            query = query.Where(x => x.Id.CompareTo(LastProcessedMediaId) > 0);
        return await query
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ChatId?> Repair(string id, CancellationToken cancellationToken)
    {
        // Returns the chat to re-index, or null when the media is left as it is
        var mediaId = MediaId.Parse(id);
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is null || media.ContentType != MediaTypeNames.Application.Octet)
            return null;
        if (media.Length > MaxReadableLength || !ChatId.TryParse(mediaId.Scope, out var chatId))
            return null;

        var stream = await BlobStorage.Read(media.BlobId, cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return null;

        byte[] data;
        await using (stream.ConfigureAwait(false)) {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            data = buffer.ToArray();
        }
        var contentType = BlobContentTypeDetector.Detect(data);
        if (!MediaTypeExt.IsHeif(contentType))
            return null;
        // An oversized one stays a file, same as a new upload of it would
        if (HeifReader.ReadDisplaySize(data) is not { } size || !ImageLimits.IsWithinStoredImageBounds(size))
            return null;

        media = media with { ContentType = contentType, Width = size.Width, Height = size.Height };
        await Commander
            .Call(new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Update = media }), true, cancellationToken)
            .ConfigureAwait(false);
        Console.Log($"{id}: {contentType} {size.Width}x{size.Height}");
        return chatId;
    }
}
