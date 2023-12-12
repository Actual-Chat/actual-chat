using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Hub = Microsoft.AspNetCore.SignalR.Hub;

namespace ActualChat.Audio;

public class AudioHubBackend(IServiceProvider services) : Hub
{
    private AudioStreamServer AudioStreamServer { get; } = services.GetRequiredService<AudioStreamServer>();
    private TranscriptStreamServer TranscriptStreamServer { get; } = services.GetRequiredService<TranscriptStreamServer>();
    private IHostApplicationLifetime HostApplicationLifetime { get; } = services.GetRequiredService<IHostApplicationLifetime>();

    public async IAsyncEnumerable<byte[]> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        await foreach (var chunk in stream.ConfigureAwait(false))
            yield return chunk;
    }

    public async IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
        await foreach (var chunk in stream.ConfigureAwait(false))
            yield return chunk;
    }

    public async Task WriteAudioStream(
        string streamId,
        IAsyncEnumerable<byte[]> stream) // No CancellationToken argument here, otherwise SignalR binder fails!
    {
        // Do not accept new audio streams in terminating state
        if (HostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
            Context.Abort();

        var cancellationToken = Context.GetHttpContext()!.RequestAborted;
        await AudioStreamServer.Write(streamId, stream, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTranscriptDiffStream(
        string streamId,
        IAsyncEnumerable<TranscriptDiff> stream) // No CancellationToken argument here, otherwise SignalR binder fails!
    {
        // Do not accept new transcript streams in terminating state
        if (HostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
            Context.Abort();

        var cancellationToken = Context.GetHttpContext()!.RequestAborted;
        await TranscriptStreamServer.Write(streamId, stream, cancellationToken).ConfigureAwait(false);
    }
}
