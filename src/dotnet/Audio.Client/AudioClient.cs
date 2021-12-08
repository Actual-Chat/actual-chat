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

    public AudioClient(IServiceProvider services)
        : base("api/hub/audio", services)
    { }

    public async Task<AudioSource> GetAudio(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio: StreamId = {StreamId}, SkipTo = {SkipTo}", streamId, skipTo);
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var audioStream = HubConnection
            .StreamAsync<AudioStreamPart>("GetAudioStream", streamId, skipTo, cancellationToken)
            .Buffer(StreamBufferSize, cancellationToken);
        var audioLog = Services.LogFor<AudioSource>();
        var audio = new AudioSource(audioStream, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        Log.LogDebug("GetAudio: Exited; StreamId = {StreamId}, SkipTo = {SkipTo}", streamId, skipTo);
        return audio;
    }

    public async IAsyncEnumerable<BlobPart> GetAudioBlobStream(
        StreamId streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudioBlobStream: StreamId = {StreamId}", streamId);
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var blobParts = HubConnection
            .StreamAsync<BlobPart>("GetAudioBlobStream", streamId, cancellationToken)
            .Buffer(StreamBufferSize, cancellationToken);
        await foreach (var blobPart in blobParts.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return blobPart;
        Log.LogDebug("GetAudioBlobStream: Exited; StreamId = {StreamId}", streamId);
    }

    public async Task RecordSourceAudio(
        Session session,
        AudioRecord record,
        IAsyncEnumerable<BlobPart> blobStream,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("RecordSourceAudio: Record = {Record}", record);
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        await HubConnection.SendAsync("RecordSourceAudio",
                session,
                record,
                blobStream.Buffer(StreamBufferSize, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        Log.LogDebug("RecordSourceAudio: Exited; Record = {Record}", record);
    }

    public async IAsyncEnumerable<TranscriptUpdate> GetTranscriptStream(
        StreamId streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogDebug("GetTranscriptStream: StreamId = {StreamId}", streamId);
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var updates = HubConnection
            .StreamAsync<TranscriptUpdate>("GetTranscriptStream", streamId, cancellationToken)
            .Buffer(StreamBufferSize, cancellationToken);
        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return update;
        Log.LogDebug("GetTranscriptStream: Exited; StreamId = {StreamId}", streamId);
    }
}
