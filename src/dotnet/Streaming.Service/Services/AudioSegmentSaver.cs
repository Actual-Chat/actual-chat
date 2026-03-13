using ActualChat.Audio;
using ActualChat.Media;

namespace ActualChat.Streaming.Services;

public sealed class AudioSegmentSaver(IServiceProvider services) : AudioProcessorBase(services)
{
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    private ICommander Commander => field ??= Services.Commander();

    public async Task<string> Save(
        ClosedAudioSegment closedAudioSegment,
        CancellationToken cancellationToken)
    {
        var streamIndex = closedAudioSegment.StreamId.Replace($"{closedAudioSegment.AudioRecord.StreamId}-", "");
        var blobId = BlobPath.Format(BlobScope.AudioRecord, closedAudioSegment.AudioRecord.StreamId.Value, streamIndex + ".webm");

        var converter = new WebMStreamConverter(Clocks, Log);
        var audioSource = closedAudioSegment.Audio;
        var byteStream = converter.ToByteStream(audioSource, cancellationToken);
        var blobStorage = Blobs[BlobScope.AudioRecord];
        await blobStorage.UploadByteStream(blobId, byteStream, cancellationToken).ConfigureAwait(false);
        return blobId;
    }

    public async Task<MediaId> SaveAndCreateMedia(
        ClosedAudioSegment closedSegment,
        ChatId chatId,
        Moment beginsAt,
        Moment recordedAt,
        CancellationToken cancellationToken)
    {
        // Save audio blob
        var blobId = await Save(closedSegment, cancellationToken).ConfigureAwait(false);

        // Create Media record with timing metadata
        var mediaId = MediaId.New(chatId.Value);
        var endsAt = beginsAt + closedSegment.Duration;
        var contentEndsAt = beginsAt + closedSegment.AudibleDuration;
        contentEndsAt = Moment.Min(endsAt, contentEndsAt);

        var media = new MediaFull(mediaId) {
            BlobId = blobId,
            ContentType = "audio/webm",
            BeginsAt = beginsAt,
            EndsAt = endsAt,
            ContentEndsAt = contentEndsAt,
            ClientSideBeginsAt = recordedAt,
        };
        var command = new MediaBackend_Change(mediaId, null, Change.Create(media));
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);

        return mediaId;
    }
}
