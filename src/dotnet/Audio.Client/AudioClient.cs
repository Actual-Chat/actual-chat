using ActualChat.Media;
using ActualChat.SignalR.Client;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Audio.Client;

public class AudioClient : HubClientBase,
    IAudioStreamer,
    ITranscriptStreamer
{
    private const int StreamBufferSize = 64;

    private ILogger AudioSourceLog { get; }

    public AudioClient(IServiceProvider services)
        : base("api/hub/audio", services)
        => AudioSourceLog = Services.LogFor<AudioSource>();

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
        var (formatTask, frames) = audioStream.ToMediaFrames(cancellationToken);
        var audio = new AudioSource(formatTask, frames, AudioSourceLog, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        Log.LogDebug("GetAudio: Exited; StreamId = {StreamId}, SkipTo = {SkipTo}", streamId, skipTo);
        return audio;
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
