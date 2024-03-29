using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.Security;
using ActualChat.Transcription;
using ActualLab.Rpc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Hub = Microsoft.AspNetCore.SignalR.Hub;

namespace ActualChat.Streaming.Services;

public class StreamHub(IServiceProvider services) : Hub
{
    private static readonly Task<string> PongTask = Task.FromResult("Pong");

    private readonly bool _preferMeshNode = services.HostInfo().HasRole(HostRole.OneServer);

    private MeshNode MeshNode { get; } = services.MeshNode();
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private IHostApplicationLifetime HostLifetime { get; } = services.HostLifetime();
    private IStreamingBackend Backend { get; } = services.GetRequiredService<IStreamingBackend>();
    private OtelMetrics Metrics { get; } = services.Metrics();
    private ILogger Log { get; } = services.LogFor<StreamHub>();

    public static Task<string> Ping()
        => PongTask;

    public async IAsyncEnumerable<byte[]> GetAudio(
        string streamId,
        TimeSpan skipTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await Backend.GetAudio(new StreamId(streamId), skipTo, cancellationToken).ConfigureAwait(false);
        if (stream == null)
            yield break;

        await foreach (var chunk in stream.ConfigureAwait(false))
            yield return chunk;
    }

    public async IAsyncEnumerable<TranscriptDiff> GetTranscript(
        string streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await Backend.GetTranscript(new StreamId(streamId), cancellationToken).ConfigureAwait(false);
        if (stream == null)
            yield break;

        await foreach (var diff in stream.ConfigureAwait(false))
            yield return diff;
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

        var chatIdTyped = new ChatId(chatId);
        var repliedChatEntryIdTyped = new ChatEntryId(repliedChatEntryId);
        var httpContext = Context.GetHttpContext()!;
        var session = GetSessionFromToken(sessionToken) ?? httpContext.GetSessionFromCookie();

        using var stopCts = NewStopTokenSource(httpContext);
        if (stopCts.IsCancellationRequested)
            return;

        stopCts.CancelAfter(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        var nodes = MeshWatcher.State.Value.NodesByRole[HostRole.AudioBackend];
        if (nodes.Length == 0) {
            Log.LogError("No nodes serving {Role} role!", HostRole.AudioBackend);
            return; // No backends
        }

        var nodeRef = _preferMeshNode ? MeshNode.Ref : nodes.GetRandom().Ref;
        var streamId = new StreamId(nodeRef, Generate.Option);
        var audioRecord = new AudioRecord(streamId, session, chatIdTyped, clientStartOffset, repliedChatEntryIdTyped);
        Log.LogInformation("ProcessAudio: {AudioRecord}", audioRecord);
        var frames = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * Constants.Audio.OpusFrameDurationMs), // we support only 20-ms packets
                Duration = Constants.Audio.OpusFrameDuration,
            })
            .TrimOnCancellation(stopCts.Token);
        var frameStream = RpcStream.New(frames);
        await Backend
            .ProcessAudio(audioRecord, preSkipFrames, frameStream, CancellationToken.None)
            .SilentAwait(false);
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        Metrics.AudioLatency.Record((float)latency.TotalMilliseconds);
        return Task.CompletedTask;
    }

    // Backward compatibility

    [Obsolete("2024.02: Remains for backward compability.")]
    public IAsyncEnumerable<byte[]> GetAudioStream(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
        => GetAudio(streamId, skipTo, cancellationToken);

    public IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(string streamId, CancellationToken cancellationToken)
        => GetTranscript(streamId, cancellationToken);

    [Obsolete("2024.02: Remains for backward compability.")]
    public Task ReportLatency(TimeSpan latency, CancellationToken cancellationToken)
        => ReportAudioLatency(latency, cancellationToken);

    // Private methods

    private CancellationTokenSource NewStopTokenSource(HttpContext httpContext)
    {
        var stopCts = httpContext.RequestAborted.LinkWith(HostLifetime.ApplicationStopping);
        if (stopCts.IsCancellationRequested && HostLifetime.ApplicationStopping.IsCancellationRequested)
            Context.Abort();
        return stopCts;
    }

    private Session? GetSessionFromToken(string sessionToken)
        => sessionToken.IsNullOrEmpty() ? null
            : SecureTokensBackend.ParseSessionToken(sessionToken);
}
