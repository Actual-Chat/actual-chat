using ActualChat.Audio.Db;
using ActualChat.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Audio.Processing;

public sealed class AudioSegmentSaver
{
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

    private readonly IBlobStorageProvider _blobs;
    private readonly ILogger<AudioSegmentSaver> _log;

    public AudioSegmentSaver(
        IServiceProvider services,
        IBlobStorageProvider blobs,
        ILogger<AudioSegmentSaver>? log = null)
    {
        _blobs = blobs;
        _log = log ?? NullLogger<AudioSegmentSaver>.Instance;
    }

    public async Task<string> Save(
        AudioSegment audioSegment,
        CancellationToken cancellationToken)
    {
        var p = audioSegment ?? throw new ArgumentNullException(nameof(audioSegment));
        var streamIndex = ((string) p.StreamId).Replace($"{p.AudioRecord.Id}-", "", StringComparison.Ordinal);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, p.AudioRecord.Id, streamIndex + ".webm");

        await SaveBlob(blobId, audioSegment.AudioSource, cancellationToken).ConfigureAwait(false);
        return blobId;
    }

    public async Task<string> Save(
        OpenAudioSegment openAudioSegment,
        CancellationToken cancellationToken)
    {
        var p = openAudioSegment ?? throw new ArgumentNullException(nameof(openAudioSegment));
        var streamIndex = ((string) p.StreamId).Replace($"{p.AudioRecord.Id}-", "", StringComparison.Ordinal);
        var blobId = BlobPath.Format(BlobScope.AudioRecord, p.AudioRecord.Id, streamIndex + ".webm");

        await SaveBlob(blobId, openAudioSegment.Source, cancellationToken).ConfigureAwait(false);
        return blobId;
    }

    private async Task SaveBlob(
        string blobId,
        AudioSource source,
        CancellationToken cancellationToken)
    {
        var blobStorage = _blobs.GetBlobStorage(BlobScope.AudioRecord);
        await using var stream = MemoryStreamManager.GetStream(nameof(AudioSegmentSaver));
        var header = Convert.FromBase64String(source.Format.CodecSettings);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await foreach (var audioFrame in source.Frames.WithCancellation(cancellationToken))
            await stream.WriteAsync(audioFrame.Data, cancellationToken).ConfigureAwait(false);

        stream.Position = 0;
        await blobStorage.WriteAsync(blobId, stream, append: false, cancellationToken).ConfigureAwait(false);
    }
}
