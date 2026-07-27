using ActualChat.Audio;
using ActualChat.Security;
using ActualLab.Rpc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Hub = Microsoft.AspNetCore.SignalR.Hub;

namespace ActualChat.Streaming.Services;

/// <summary>
/// SignalR hub kept only for legacy clients that still push audio via
/// <see cref="ProcessAudioChunks"/>. Current clients use Fusion RPC —
/// <see cref="ILiveAudioStreams"/> / <see cref="ILiveVideoStreams"/>.
/// </summary>
public class StreamHub(IServiceProvider services) : Hub
{
    private static readonly Task<string> PongTask = Task.FromResult("Pong");

    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private IHostApplicationLifetime HostLifetime { get; } = services.HostLifetime();
    private IAudioStreamingBackend Backend { get; } = services.GetRequiredService<IAudioStreamingBackend>();
    private ILogger Log { get; } = services.LogFor<StreamHub>();

    // Currently unused
    [Obsolete("2026.04: No clients connect to StreamHub anymore")]
    public static Task<string> Ping()
        => PongTask;

    [Obsolete("2026.04: Use ILiveAudioStreams.PushStream via RPC")]
    public Task ProcessAudioChunks(
        string sessionToken, string? chatId, string? repliedChatEntryId,
        double sourceStartOffsetSeconds, int preSkip,
        IAsyncEnumerable<byte[][]> audioStream)
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        => ProcessAudio(
            sessionToken,
            chatId,
            repliedChatEntryId,
            sourceStartOffsetSeconds,
            preSkip,
            audioStream.SelectMany(c => c.AsAsyncEnumerable()));

    // Private methods

    private async Task ProcessAudio(
        string sessionToken,
        string? chatId,
        string? repliedEntryId,
        double sourceStartOffsetSeconds,
        int preSkip,
        IAsyncEnumerable<byte[]> audioStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!

        var chatIdTyped = ChatId.Parse(chatId);
        var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedEntryId);
        var httpContext = Context.GetHttpContext()!;
        var session = GetSessionFromToken(sessionToken) ?? httpContext.GetSessionFromCookie();

        using var stopCts = CreateStopTokenSource(httpContext);
        if (stopCts.IsCancellationRequested)
            return;

        stopCts.CancelAfter(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        var audioRecord = new AudioRecord(streamId, session, chatIdTyped, sourceStartOffsetSeconds, repliedEntryIdTyped);

        Log.LogInformation("ProcessAudio: {AudioRecord}", audioRecord);
        var frames = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * Constants.Audio.OpusFrameDurationMs), // we support only 20-ms packets
                Duration = Constants.Audio.OpusFrameDuration,
            })
            .SuppressCancellation(stopCts.Token);
        var frameStream = RpcStream.New(frames);
        await Backend
            .ProcessAudio(audioRecord, preSkip, frameStream, CancellationToken.None)
            .SilentAwait(false);
    }

    private CancellationTokenSource CreateStopTokenSource(HttpContext httpContext)
    {
        var hostStopToken = HostLifetime.StopToken();
        var stopCts = hostStopToken.LinkWith(httpContext.RequestAborted);
        if (stopCts.IsCancellationRequested && hostStopToken.IsCancellationRequested)
            Context.Abort();
        return stopCts;
    }

    private Session? GetSessionFromToken(string sessionToken)
        => sessionToken.IsNullOrEmpty() ? null
            : SecureTokensBackend.DecryptSessionToken(sessionToken);
}
