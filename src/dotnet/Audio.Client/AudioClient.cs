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
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio: StreamId = {StreamId}, SkipTo = {SkipTo}", streamId.Value, skipTo.ToShortString());
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var audioStream = connection
            .StreamAsync<byte[]>("GetAudioStream", streamId.Value, skipTo, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        var frameStream = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });
        var audio = new AudioSource(Task.FromResult(AudioSource.DefaultFormat),
            frameStream,
            TimeSpan.Zero,
            AudioSourceLog,
            cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        Log.LogDebug("GetAudio: Exited; StreamId = {StreamId}, SkipTo = {SkipTo}", streamId.Value, skipTo.ToShortString());
        return audio;
    }

    public async IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        Symbol streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogDebug("GetTranscriptDiffStream: StreamId = {StreamId}", streamId.Value);
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var updates = connection
            .StreamAsync<Transcript>("GetTranscriptDiffStream", streamId.Value, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        await foreach (var update in updates.ConfigureAwait(false))
            yield return update;
        Log.LogDebug("GetTranscriptDiffStream: Exited; StreamId = {StreamId}", streamId.Value);
    }
}
