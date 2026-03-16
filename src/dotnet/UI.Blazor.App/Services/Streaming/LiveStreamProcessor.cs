using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages live stream connection with automatic reconnection on disconnect.
/// </summary>
public sealed class LiveStreamProcessor : WorkerBase
{
    private static bool DebugMode => Constants.DebugMode.LiveStreaming;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(0.25);

    private ILogger Log { get; }
    private ILogger? DebugLog { get; }

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public ChatId ChatId { get; }
    public LiveStreamSettings Settings { get; }

    public event Action<LiveStreamInfo, IAsyncEnumerable<byte[]>>? StreamStarted;

    public LiveStreamProcessor(IServiceProvider services,
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationTokenSource? stopTokenSource = null
        ) : base(stopTokenSource)
    {
        Log = services.LogFor(GetType());
        DebugLog = DebugMode ? Log : null;

        Services = services;
        Session = session;
        ChatId = chatId;
        Settings = settings;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var liveStreams = Services.GetRequiredService<ILiveStreams>();
        var demuxerLog = Services.LogFor<LiveStreamDemuxer>();
        while (true) {
            try {
                DebugLog?.LogInformation("-> LiveStreams.GetLiveStream({ChatId})", ChatId);
                var stream = await liveStreams.GetLiveStream(Session, ChatId, Settings, cancellationToken).ConfigureAwait(false);
                DebugLog?.LogInformation("<- LiveStreams.GetLiveStream({ChatId})", ChatId);

                var demuxer = new LiveStreamDemuxer(stream, demuxerLog, cancellationToken.CreateLinkedTokenSource());
                await using var _ = demuxer.ConfigureAwait(false);
                demuxer.StreamStarted += (info, frames) => StreamStarted?.Invoke(info, frames);

                DebugLog?.LogInformation("Demuxing live stream for {ChatId}...", ChatId);
                await demuxer.Run().ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogWarning(e, "Failed for chat {ChatId}, will reconnect in {Delay}",
                    ChatId, ReconnectDelay.ToShortString());
            }

            // Wait before reconnecting
            await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
