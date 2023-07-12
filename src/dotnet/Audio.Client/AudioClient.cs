using System.Buffers;
using ActualChat.Rpc;
using ActualChat.SignalR;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;
using Stl.Rpc;

namespace ActualChat.Audio;

public class AudioClient : HubClientBase,
    IAudioStreamer,
    ITranscriptStreamer
{
    private ILogger AudioSourceLog { get; }

    public int StreamBufferSize { get; init; } = 64;

    public AudioClient(IServiceProvider services)
        : base("api/hub/audio", services.GetRequiredService<RpcDependentReconnectDelayer>(), services)
        => AudioSourceLog = services.LogFor<AudioSource>();

    public async Task<AudioSource> GetAudio(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("GetAudio: StreamId = {StreamId}, SkipTo = {SkipTo}", streamId.Value, skipTo.ToShortString());
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        var audioStream = connection
            .StreamAsync<byte[]>("GetAudioStream", streamId.Value, skipTo, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);

        var (headerDataTask, dataStream) = audioStream.SplitHead(cancellationToken);
        var frameStream = dataStream
            .Select((data, i) => new AudioFrame {
                Data = data,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
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
        Log.LogDebug("GetAudio: Exited; StreamId = {StreamId}, SkipTo = {SkipTo}", streamId.Value, skipTo.ToShortString());
        return audio;
    }

    public async Task ReportLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        await connection.SendAsync("ReportLatency", latency, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(
        Symbol streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Log.LogDebug("GetTranscriptDiffStream: StreamId = {StreamId}", streamId.Value);
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        var diffs = connection
            .StreamAsync<TranscriptDiff>("GetTranscriptDiffStream", streamId.Value, cancellationToken)
            .WithBuffer(StreamBufferSize, cancellationToken);
        await foreach (var diff in diffs.ConfigureAwait(false))
            yield return diff;
        Log.LogDebug("GetTranscriptDiffStream: Exited; StreamId = {StreamId}", streamId.Value);
    }
}
