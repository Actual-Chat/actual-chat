using ActualChat.Diagnostics;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

public class StreamServer(IServiceProvider services) : IStreamServer
{
    private IStreamingBackend Backend { get; } = services.GetRequiredService<IStreamingBackend>();

    public async Task<RpcStream<byte[]>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        // We must return another RpcStream here - they aren't "shareable"
        var source = await Backend.GetAudio(StreamId.Parse(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        return source == null ? null : RpcStream.New(source.AsAsyncEnumerable());
    }

    public async Task<RpcStream<TranscriptDiff>?> GetTranscript(string streamId, CancellationToken cancellationToken)
    {
        // We must return another RpcStream here - they aren't "shareable"
        var source = await Backend.GetTranscript(StreamId.Parse(streamId), cancellationToken).ConfigureAwait(false);
        return source == null ? null : RpcStream.New(source.AsAsyncEnumerable());
    }

    public async Task<RpcStream<TranscriptDiff>?> GetTranslatedTranscript(
        TranslationId translationId,
        string streamId,
        CancellationToken cancellationToken)
    {
        // We must return another RpcStream here - they aren't "shareable"
        var source = await Backend.GetTranslatedTranscript(StreamId.Parse(streamId), translationId, cancellationToken).ConfigureAwait(false);
        return source == null ? null : RpcStream.New(source.AsAsyncEnumerable());
    }

    public async Task<RpcStream<StringDiff>?> GetTranslation(string streamId, CancellationToken cancellationToken)
    {
        var source = await Backend.GetTranslation(StreamId.Parse(streamId), cancellationToken).ConfigureAwait(false);
        return source == null ? null : RpcStream.New(source.AsAsyncEnumerable());
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        AppMeters.AudioLatency.Record((float)latency.TotalMilliseconds);
        return Task.CompletedTask;
    }
}
