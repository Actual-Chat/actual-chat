using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages live stream connection with automatic reconnection on disconnect
/// using <see cref="ResilientStream{T}"/>.
/// </summary>
public sealed class ListeningStreamProcessor : WorkerBase
{
    private static bool DebugMode => Constants.DebugMode.LiveStreaming;

    private ILogger Log { get; }
    private ILogger? DebugLog { get; }

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public ChatId ChatId { get; }

    public event Action<LiveAudioStreamInfo, TimeSpan, IAsyncEnumerable<AudioFrame>>? StreamStarted;

    public ListeningStreamProcessor(IServiceProvider services,
        Session session,
        ChatId chatId,
        CancellationTokenSource? stopTokenSource = null
        ) : base(stopTokenSource)
    {
        Log = services.LogFor(GetType());
        DebugLog = DebugMode ? Log : null;

        Services = services;
        Session = session;
        ChatId = chatId;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var liveStreams = Services.GetRequiredService<ILiveAudioStreams>();
        var demuxerLog = Services.LogFor<AudioStreamDemuxer>();

        var itemStream = new ResilientStream<MuxedStreamItem> {
            Provider = async ct => {
                DebugLog?.LogInformation("-> LiveStreams.GetListeningStream({ChatId})", ChatId);
                var stream = await liveStreams.GetListeningStream(Session, ChatId, ct).ConfigureAwait(false);
                DebugLog?.LogInformation("<- LiveStreams.GetListeningStream({ChatId})", ChatId);
                return stream;
            },
            ResetItem = Option.Some<MuxedStreamItem>(new MuxedAudioStreamReset()),
        };

        var demuxer = new AudioStreamDemuxer(itemStream, demuxerLog, cancellationToken.CreateLinkedTokenSource());
        await using var _ = demuxer.ConfigureAwait(false);
        demuxer.StreamStarted += (info, playsAt, frames) => StreamStarted?.Invoke(info, playsAt, frames);

        DebugLog?.LogInformation("Demuxing live stream for {ChatId}...", ChatId);
        await demuxer.Run().ConfigureAwait(false);
    }
}
