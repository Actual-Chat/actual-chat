using ActualChat.Live;
using ActualChat.Streaming;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages live stream connection with automatic reconnection on disconnect
/// using <see cref="ResilientStream{T}"/>.
/// </summary>
public sealed class LiveStreamProcessor : WorkerBase
{
    private static bool DebugMode => Constants.DebugMode.LiveStreaming;

    private ILogger Log { get; }
    private ILogger? DebugLog { get; }

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public ChatId ChatId { get; }
    public LiveStreamSettings Settings { get; }

    public event Action<LiveStreamInfo, TimeSpan, IAsyncEnumerable<byte[]>>? StreamStarted;

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

        var itemStream = new ResilientStream<LiveStreamItem> {
            Provider = async ct => {
                DebugLog?.LogInformation("-> LiveStreams.GetLiveStream({ChatId})", ChatId);
                var stream = await liveStreams.GetLiveStream(Session, ChatId, Settings, ct).ConfigureAwait(false);
                DebugLog?.LogInformation("<- LiveStreams.GetLiveStream({ChatId})", ChatId);
                return stream;
            },
            ResetItem = Option.Some<LiveStreamItem>(new LiveStreamReset()),
        };

        var demuxer = new LiveStreamDemuxer(itemStream, demuxerLog, cancellationToken.CreateLinkedTokenSource());
        await using var _ = demuxer.ConfigureAwait(false);
        demuxer.StreamStarted += (info, playsAt, frames) => StreamStarted?.Invoke(info, playsAt, frames);

        DebugLog?.LogInformation("Demuxing live stream for {ChatId}...", ChatId);
        await demuxer.Run().ConfigureAwait(false);
    }
}
