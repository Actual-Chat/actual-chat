using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

public class StreamServer(IServiceProvider services) : IStreamServer
{
    private IStreamingBackend Backend { get; } = services.GetRequiredService<IStreamingBackend>();
    private OtelMetrics Metrics { get; } = services.Metrics();
    private ILogger Log { get; } = services.LogFor<StreamHub>();

    public async Task<RpcStream<byte[]>?> GetAudio(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        // We must return another RpcStream here - they aren't "shareable"
        var source = await Backend.GetAudio(new StreamId(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        return source == null ? null : RpcStream.New(source.AsAsyncEnumerable());
    }

    public async Task<RpcStream<TranscriptDiff>?> GetTranscript(Symbol streamId, CancellationToken cancellationToken)
    {
        // We must return another RpcStream here - they aren't "shareable"
        var source = await Backend.GetTranscript(new StreamId(streamId), cancellationToken).ConfigureAwait(false);
        return source == null ? null : RpcStream.New(source.AsAsyncEnumerable());
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        Metrics.AudioLatency.Record((float)latency.TotalMilliseconds);
        return Task.CompletedTask;
    }
}
