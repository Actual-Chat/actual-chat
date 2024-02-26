using System.Buffers;
using ActualChat.Audio;
using ActualChat.Rpc;
using ActualChat.SignalR;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Streaming;

public class StreamHubClient(IServiceProvider services) : HubClientBase(
    "api/hub/streams",
    services.GetRequiredService<RpcDependentReconnectDelayer>(),
    services
    ), IStreamClient
{
    private static readonly int StreamBufferSize = 64;

    private ILogger? _audioSourceLog;
    private ILogger AudioSourceLog => _audioSourceLog ??= Services.LogFor<AudioSource>();

    public async Task<AudioSource> GetAudio(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("-> GetAudio(#{StreamId}, SkipTo = {SkipTo})", streamId.Value, skipTo.ToShortString());
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        var audioStream = connection
            .StreamAsync<byte[]>("GetAudio", streamId.Value, skipTo, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);

        var (headerDataTask, dataStream) = audioStream.SplitHead(cancellationToken);
        var frameStream = dataStream
            .Select((data, i) => new AudioFrame {
                Data = data,
                Offset = TimeSpan.FromMilliseconds(i * Constants.Audio.OpusFrameDurationMs), // we support only 20-ms packets
                Duration = Constants.Audio.OpusFrameDuration,
            });

        var headerData = await headerDataTask.ConfigureAwait(false);
        var headerDataSequence = new ReadOnlySequence<byte>(headerData);
        var header = ActualOpusStreamHeader.Parse(ref headerDataSequence);

        var audio = new AudioSource(
            header.CreatedAt,
            header.Format,
            frameStream,
            TimeSpan.Zero,
            AudioSourceLog,
            cancellationToken);
        Log.LogDebug("<- GetAudio(#{StreamId}, SkipTo = {SkipTo})", streamId.Value, skipTo.ToShortString());
        return audio;
    }

    public async IAsyncEnumerable<TranscriptDiff> GetTranscript(
        Symbol streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogDebug("-> GetTranscript(#{StreamId})", streamId.Value);
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        var diffs = connection
            .StreamAsync<TranscriptDiff>("GetTranscript", streamId.Value, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        await foreach (var diff in diffs.ConfigureAwait(false))
            yield return diff;
        Log.LogDebug("<- GetTranscript(#{StreamId})", streamId.Value);
    }

    public async Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        await connection.SendAsync("ReportAudioLatency", latency, cancellationToken).ConfigureAwait(false);
    }
}
