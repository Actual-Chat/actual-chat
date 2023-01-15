using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Audio;

public class AudioHub : Hub
{
    private IAudioProcessor AudioProcessor { get; }
    private IAudioStreamServer AudioStreamServer { get; }
    private ITranscriptStreamServer TranscriptStreamServer { get; }
    private SessionMiddleware SessionMiddleware { get; }
    private OtelMetrics Metrics { get; }

    public AudioHub(
        IAudioProcessor audioProcessor,
        IAudioStreamServer audioStreamServer,
        ITranscriptStreamServer transcriptStreamServer,
        SessionMiddleware sessionMiddleware,
        OtelMetrics metrics)
    {
        AudioProcessor = audioProcessor;
        AudioStreamServer = audioStreamServer;
        TranscriptStreamServer = transcriptStreamServer;
        SessionMiddleware = sessionMiddleware;
        Metrics = metrics;
    }

    public async IAsyncEnumerable<byte[]> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in stream.ConfigureAwait(false))
            yield return chunk;
    }

    public Task ReportLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        Metrics.AudioLatency.Record(latency.Ticks / 10000f);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<Transcript> GetTranscriptDiffStream(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in stream.ConfigureAwait(false))
            yield return chunk;
    }

    public async Task ProcessAudio(string sessionId, string chatId, double clientStartOffset, int preSkipFrames, IAsyncEnumerable<byte[]> audioStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!

        var httpContext = Context.GetHttpContext()!;
        var cancellationToken = httpContext.RequestAborted;
        var session = SessionMiddleware.GetSession(httpContext).Require();

        var audioRecord = new AudioRecord(new Session(session.Id), new ChatId(chatId), clientStartOffset);
        var frameStream = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });
        await AudioProcessor.ProcessAudio(audioRecord, preSkipFrames, frameStream, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<string> Ping()
        => Task.FromResult("Pong");
}
