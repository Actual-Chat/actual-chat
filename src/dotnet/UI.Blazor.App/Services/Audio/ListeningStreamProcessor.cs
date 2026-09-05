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

    private ResilientStream<MuxedAudioStreamItem>? _itemStream;
    private bool _isCatchUpConsumed;

    private ILogger Log { get; }
    private ILogger? DebugLog { get; }

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public ChatId ChatId { get; }
    public Moment CatchUpFrom { get; }

    public event Action<LiveAudioStreamInfo, TimeSpan, IAsyncEnumerable<AudioFrame>>? StreamStarted;

    public ListeningStreamProcessor(IServiceProvider services,
        Session session,
        ChatId chatId,
        Moment catchUpFrom = default,
        CancellationTokenSource? stopTokenSource = null
        ) : base(stopTokenSource)
    {
        Log = services.LogFor(GetType());
        DebugLog = DebugMode ? Log : null;

        Services = services;
        Session = session;
        ChatId = chatId;
        CatchUpFrom = catchUpFrom;
    }

    public void Break()
        // Acquire: _itemStream is published by OnRun on the worker, read here from the caller's thread.
        => Volatile.Read(ref _itemStream)?.Break();

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var liveStreams = Services.GetRequiredService<ILiveAudioStreams>();
        var clocks = Services.Clocks();
        var demuxerLog = Services.LogFor<AudioStreamDemuxer>();

        var itemStream = new ResilientStream<MuxedAudioStreamItem> {
            Provider = async ct => {
                // The anchor is for the first connection only: the server serves the trigger
                // utterance from t=0 to whoever asks, so a reconnect that still carried it would
                // re-play what the listener already heard.
                var catchUpFrom = _isCatchUpConsumed || Ptt.IsStaleWake(CatchUpFrom, clocks.ServerClock.Now)
                    ? default
                    : CatchUpFrom;
                Log.LogInformation("-> LiveStreams.GetListeningStream({ChatId}), catchUpFrom={CatchUpFrom}",
                    ChatId, catchUpFrom);
                var stream = await liveStreams.GetListeningStream(Session, ChatId, catchUpFrom, ct)
                    .ConfigureAwait(false);
                _isCatchUpConsumed = true;
                DebugLog?.LogInformation("<- LiveStreams.GetListeningStream({ChatId})", ChatId);
                return stream;
            },
            ResetItem = Option.Some<MuxedAudioStreamItem>(new MuxedAudioStreamReset()),
            IsInfinite = true,
        };
        // Release: Break() reads this from the caller's thread.
        Volatile.Write(ref _itemStream, itemStream);

        var demuxer = new AudioStreamDemuxer(itemStream, demuxerLog, cancellationToken.CreateLinkedTokenSource());
        await using var _ = demuxer.ConfigureAwait(false);
        demuxer.StreamStarted += (info, playsAt, frames) => StreamStarted?.Invoke(info, playsAt, frames);

        DebugLog?.LogInformation("Demuxing live stream for {ChatId}...", ChatId);
        await demuxer.Run().ConfigureAwait(false);
    }
}
