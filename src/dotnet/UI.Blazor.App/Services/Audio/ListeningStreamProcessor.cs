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
    private CpuTimestamp? _lastReanchorAt;

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
        var effectiveCatchUpFrom = Ptt.IsStaleWake(CatchUpFrom, clocks.ServerClock.Now) ? default : CatchUpFrom;

        var itemStream = new ResilientStream<MuxedAudioStreamItem> {
            Provider = async ct => {
                // The anchor is for the first connection only: the server serves the trigger
                // utterance from t=0 to whoever asks, so a reconnect that still carried it would
                // re-play what the listener already heard.
                var catchUpFrom = Volatile.Read(ref _isCatchUpConsumed) ? default : effectiveCatchUpFrom;
                Log.LogInformation("-> LiveStreams.GetListeningStream({ChatId}), catchUpFrom={CatchUpFrom}",
                    ChatId, catchUpFrom);
                var stream = await liveStreams.GetListeningStream(Session, ChatId, catchUpFrom, ct)
                    .ConfigureAwait(false);
                // Release: the watchdog reads this on the demuxer's thread to classify the next connection.
                Volatile.Write(ref _isCatchUpConsumed, true);
                DebugLog?.LogInformation("<- LiveStreams.GetListeningStream({ChatId})", ChatId);
                return stream;
            },
            ResetItem = Option.Some<MuxedAudioStreamItem>(new MuxedAudioStreamReset()),
            IsInfinite = true,
        };
        // Release: Break() reads this from the caller's thread.
        Volatile.Write(ref _itemStream, itemStream);

        var watchedItems = WithArrivalLagWatchdog(
            itemStream, clocks.ServerClock, effectiveCatchUpFrom, cancellationToken);
        var demuxer = new AudioStreamDemuxer(watchedItems, demuxerLog, cancellationToken.CreateLinkedTokenSource());
        await using var _ = demuxer.ConfigureAwait(false);
        demuxer.StreamStarted += (info, playsAt, frames) => StreamStarted?.Invoke(info, playsAt, frames);

        DebugLog?.LogInformation("Demuxing live stream for {ChatId}...", ChatId);
        await demuxer.Run().ConfigureAwait(false);
    }

    // Private methods

    private async IAsyncEnumerable<MuxedAudioStreamItem> WithArrivalLagWatchdog(
        ResilientStream<MuxedAudioStreamItem> source,
        MomentClock serverClock,
        Moment catchUpFrom,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Frames are stamped with their capture time (BeginsAt + Offset), so how far behind the
        // newest one arrives is the receive path's own backlog, measured before any player buffer.
        // Nothing downstream ever skips or speeds up, so a backlog that built up here would
        // otherwise stay for the rest of the session; breaking the stream re-subscribes at the
        // live edge. A catch-up connection replays from t=0 on purpose, so only live ones are
        // judged - and the Provider decides which kind the next connection is from
        // _isCatchUpConsumed, so a Reset re-reads it rather than assuming the anchor is spent.
        var isCatchUpConnection = catchUpFrom != default;
        var beginsAtByStreamIndex = new Dictionary<int, Moment>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            switch (item) {
            case MuxedAudioStreamReset:
                beginsAtByStreamIndex.Clear();
                isCatchUpConnection = catchUpFrom != default && !Volatile.Read(ref _isCatchUpConsumed);
                break;
            case MuxedAudioStreamStart start:
                beginsAtByStreamIndex[start.StreamIndex] = start.StreamInfo.BeginsAt;
                break;
            case MuxedAudioStreamEnd end:
                beginsAtByStreamIndex.Remove(end.StreamIndex);
                break;
            case MuxedAudioFrame frame when !isCatchUpConnection
                && frame.Offset >= TimeSpan.Zero
                && beginsAtByStreamIndex.TryGetValue(frame.StreamIndex, out var beginsAt):
                var lag = serverClock.Now - (beginsAt + frame.Offset);
                if (lag > Constants.Audio.ListeningMaxArrivalLag && CanReanchor()) {
                    _lastReanchorAt = CpuTimestamp.Now;
                    Log.LogWarning(
                        "Listening in #{ChatId}: frames arrive {LagMs:F0}ms late, re-subscribing at the live edge",
                        ChatId, lag.TotalMilliseconds);
                    beginsAtByStreamIndex.Clear();
                    source.Break();
                }
                break;
            }
            yield return item;
        }
    }

    private bool CanReanchor()
        => _lastReanchorAt is not { } lastReanchorAt
            || lastReanchorAt.Elapsed >= Constants.Audio.ListeningReanchorMinPeriod;
}
