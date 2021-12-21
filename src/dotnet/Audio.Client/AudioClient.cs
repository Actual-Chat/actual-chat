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
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio: StreamId = {StreamId}, SkipTo = {SkipTo}", streamId, skipTo);
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var audioStream = HubConnection
            .StreamAsync<AudioStreamPart>("GetAudioStream", streamId, skipTo, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        var audioLog = Services.LogFor<AudioSource>();
        var audio = new AudioSource(audioStream, audioLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        Log.LogDebug("GetAudio: Exited; StreamId = {StreamId}, SkipTo = {SkipTo}", streamId, skipTo);
        return audio;
    }

    public async IAsyncEnumerable<BlobPart> GetAudioBlobStream(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudioBlobStream: StreamId = {StreamId}", streamId);
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var blobParts = HubConnection
            .StreamAsync<BlobPart>("GetAudioBlobStream", streamId, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        await foreach (var blobPart in blobParts.ConfigureAwait(false))
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
                blobStream.WithBuffer(StreamBufferSize, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        Log.LogDebug("RecordSourceAudio: Exited; Record = {Record}", record);
    }

    public async IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogDebug("GetTranscriptDiffStream: StreamId = {StreamId}", streamId);
        await EnsureConnected(CancellationToken.None).ConfigureAwait(false);
        var updates = HubConnection
            .StreamAsync<Transcript>("GetTranscriptDiffStream", streamId, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        await foreach (var update in updates.ConfigureAwait(false))
            yield return update;
        Log.LogDebug("GetTranscriptDiffStream: Exited; StreamId = {StreamId}", streamId);
    }
}
