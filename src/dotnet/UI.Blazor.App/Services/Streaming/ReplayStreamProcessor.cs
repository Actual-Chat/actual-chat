using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Connects to the server-side replay stream and demuxes it into individual audio streams.
/// Unlike <see cref="LiveStreamProcessor"/>, does not auto-reconnect on failure.
/// </summary>
public sealed class ReplayStreamProcessor : WorkerBase
{
    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public ChatId ChatId { get; }
    public Moment StartAt { get; }
    public TimeSpan Offset { get; }

    public event Action<LiveStreamInfo, TimeSpan, IAsyncEnumerable<byte[]>>? StreamStarted;

    public ReplayStreamProcessor(
        IServiceProvider services,
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan offset,
        CancellationTokenSource? stopTokenSource = null)
        : base(stopTokenSource)
    {
        Log = services.LogFor(GetType());
        Services = services;
        Session = session;
        ChatId = chatId;
        StartAt = startAt;
        Offset = offset;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var liveStreams = Services.GetRequiredService<ILiveStreams>();
        var demuxerLog = Services.LogFor<LiveStreamDemuxer>();

        try {
            Log.LogInformation("-> LiveStreams.GetReplayStream({ChatId}, {StartAt}, {Offset})",
                ChatId, StartAt, Offset);
            var stream = await liveStreams
                .GetReplayStream(Session, ChatId, StartAt, Offset, cancellationToken)
                .ConfigureAwait(false);
            Log.LogInformation("<- LiveStreams.GetReplayStream({ChatId})", ChatId);

            var demuxer = new LiveStreamDemuxer(stream, demuxerLog, cancellationToken.CreateLinkedTokenSource());
            await using var _ = demuxer.ConfigureAwait(false);
            demuxer.StreamStarted += (info, playsAt, frames) => StreamStarted?.Invoke(info, playsAt, frames);

            Log.LogInformation("Demuxing replay stream for {ChatId}...", ChatId);
            await demuxer.Run().ConfigureAwait(false);
            Log.LogInformation("Replay stream completed for {ChatId}", ChatId);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Replay stream failed for chat {ChatId}", ChatId);
        }
    }
}
