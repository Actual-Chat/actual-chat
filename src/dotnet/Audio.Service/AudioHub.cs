using ActualChat.Security;
using ActualChat.Transcription;
using ActualChat.Web;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Audio;

public class AudioHub(IServiceProvider services) : Hub
{
    private IHostApplicationLifetime HostApplicationLifetime { get; } = services.GetRequiredService<IHostApplicationLifetime>();
    private IAudioProcessor AudioProcessor { get; } = services.GetRequiredService<IAudioProcessor>();
    private IAudioStreamServer AudioStreamServer { get; } = services.GetRequiredService<IAudioStreamServer>();
    private ITranscriptStreamServer TranscriptStreamServer { get; } = services.GetRequiredService<ITranscriptStreamServer>();
    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private OtelMetrics Metrics { get; } = services.GetRequiredService<OtelMetrics>();
    private ILogger Log { get; } = services.LogFor<AudioHub>();

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

    public async IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in stream.ConfigureAwait(false))
            yield return chunk;
    }

    public Task ProcessAudioChunks(
        string sessionToken,
        string chatId,
        string repliedChatEntryId,
        double clientStartOffset,
        int preSkipFrames,
        IAsyncEnumerable<byte[][]> audioStream)
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        => ProcessAudio(
            sessionToken,
            chatId,
            repliedChatEntryId,
            clientStartOffset,
            preSkipFrames,
            audioStream.SelectMany(c => c.AsAsyncEnumerable()));

    public async Task ProcessAudio(
        string sessionToken,
        string chatId,
        string repliedChatEntryId,
        double clientStartOffset,
        int preSkipFrames,
        IAsyncEnumerable<byte[]> audioStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!

        // Do not accept new audio streams in terminating state
        if (HostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
            Context.Abort();

        var httpContext = Context.GetHttpContext()!;
        var session = GetSessionFromToken(sessionToken) ?? httpContext.GetSessionFromCookie();

        var audioRecord = AudioRecord.New(new Session(session.Id), new ChatId(chatId), clientStartOffset, new ChatEntryId(repliedChatEntryId));
        var frameStream = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });

        var trimDuration = Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5);
        var cancelDuration = Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(10);

        using var cancelCts = new CancellationTokenSource(cancelDuration);
        using var trimCts = new CancellationTokenSource(trimDuration);
        frameStream = frameStream.TrimOnCancellation(trimCts.Token);

        await AudioProcessor
            .ProcessAudio(audioRecord, preSkipFrames, frameStream, cancelCts.Token)
            .ConfigureAwait(false);
    }

    public static Task<string> Ping()
        => Task.FromResult("Pong");

    private Session? GetSessionFromToken(string sessionToken)
        => sessionToken.IsNullOrEmpty() ? null
            : SecureTokensBackend.ParseSessionToken(sessionToken);
}
