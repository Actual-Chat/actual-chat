using ActualChat.Blobs;
using ActualChat.SignalR.Client;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Audio.Client;

public class AudioClient : HubClientBase,
    ISourceAudioRecorder,
    IAudioStreamer,
    IAudioSourceStreamer,
    ITranscriptStreamer
{
    private const int StreamBufferSize = 64;

    public AudioClient(IServiceProvider services) : base(services, "api/hub/audio") { }

    public async Task<AudioSource> GetAudio(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var audioStream = HubConnection
            .StreamAsync<AudioStreamPart>("GetAudioStream", streamId, skipTo, cancellationToken)
            .Buffer(StreamBufferSize, cancellationToken);
        var audio = new AudioSource(audioStream, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    public async IAsyncEnumerable<BlobPart> GetAudioBlobStream(
        StreamId streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var blobParts = HubConnection
            .StreamAsync<BlobPart>("GetAudioBlobStream", streamId, cancellationToken)
            .Buffer(StreamBufferSize, cancellationToken);
        await foreach (var blobPart in blobParts.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return blobPart;
    }

    public async Task RecordSourceAudio(
        Session session,
        AudioRecord audioRecord,
        IAsyncEnumerable<BlobPart> blobStream,
        CancellationToken cancellationToken)
    {
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        await HubConnection.SendAsync("RecordSourceAudio",
                session,
                audioRecord,
                blobStream.Buffer(StreamBufferSize, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TranscriptUpdate> GetTranscriptStream(
        StreamId streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var updates = HubConnection
            .StreamAsync<TranscriptUpdate>("GetTranscriptStream", streamId, cancellationToken)
            .Buffer(StreamBufferSize, cancellationToken);
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return update;
    }
}
