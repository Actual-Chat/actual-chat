using ActualChat.Security;
using ActualChat.Transcription;
using ActualChat.Web;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

public class AudioHub : Hub
{
    private IServiceProvider Services { get; }
    private IAudioProcessor AudioProcessor { get; }
    private IAudioStreamServer AudioStreamServer { get; }
    private ITranscriptStreamServer TranscriptStreamServer { get; }
    private ISecureTokensBackend SecureTokensBackend { get; }
    private OtelMetrics Metrics { get; }

    public AudioHub(IServiceProvider services)
    {
        Services = services;
        Metrics = services.GetRequiredService<OtelMetrics>();
        AudioProcessor = services.GetRequiredService<IAudioProcessor>();
        AudioStreamServer = services.GetRequiredService<IAudioStreamServer>();
        TranscriptStreamServer = services.GetRequiredService<ITranscriptStreamServer>();
        SecureTokensBackend = services.GetRequiredService<ISecureTokensBackend>();
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

        var httpContext = Context.GetHttpContext()!;
        var session = GetSessionFromToken(sessionToken) ?? httpContext.GetSession();

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

    public Task<string> Ping()
        => Task.FromResult("Pong");

    private Session? GetSessionFromToken(string recorderToken)
    {
        // [Obsolete("2023.07: Legacy clients use 'default' value.")]
        if (recorderToken.IsNullOrEmpty() || OrdinalEquals(recorderToken, "default"))
            return null;

        if (recorderToken.Length < 50)
            return new Session(recorderToken).RequireValid();; // old clients don't send proper session token (usually has length > 150)

        return SecureTokensBackend.ParseSessionToken(recorderToken);
    }
}
