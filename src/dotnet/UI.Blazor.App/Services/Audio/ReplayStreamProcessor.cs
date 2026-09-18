using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Connects to the server-side replay stream and demuxes it into individual audio streams.
/// Unlike <see cref="ListeningStreamProcessor"/>, does not auto-reconnect on failure.
/// </summary>
public sealed class ReplayStreamProcessor : WorkerBase
{
    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public ChatId ChatId { get; }
    public Moment StartAt { get; }
    public TimeSpan RewindOffset { get; }
    public double Speed { get; }
    public Func<CancellationToken, Task<Language?>>? DubLanguageProvider { get; init; }
    public Tracer Tracer { get; init; } = Tracer.None;

    public event Action<LiveAudioStreamInfo, TimeSpan, IAsyncEnumerable<AudioFrame>>? StreamStarted;

    public ReplayStreamProcessor(
        IServiceProvider services,
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed = 1.0,
        CancellationTokenSource? stopTokenSource = null)
        : base(stopTokenSource)
    {
        Log = services.LogFor(GetType());
        Services = services;
        Session = session;
        ChatId = chatId;
        StartAt = startAt;
        RewindOffset = rewindOffset;
        Speed = speed;
    }

    public static Tracer GetTrackTracer(Tracer tracer, LiveAudioStreamInfo streamInfo)
        => tracer[$"#{streamInfo.EntryId?.LocalId.ToString() ?? streamInfo.StreamId}"];

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var liveStreams = Services.GetRequiredService<ILiveAudioStreams>();
        var demuxerLog = Services.LogFor<AudioStreamDemuxer>();

        try {
            var dubLanguage = DubLanguageProvider == null
                ? null
                : await DubLanguageProvider.Invoke(cancellationToken).ConfigureAwait(false);
            Tracer.Point($"Stream: dub language resolved ({dubLanguage?.ToString() ?? "none"})");
            Log.LogInformation(
                "-> LiveStreams.GetReplayStream({ChatId}, {StartAt}, {RewindOffset}, speed={Speed}, dub={DubLanguage})",
                ChatId, StartAt, RewindOffset, Speed, dubLanguage);
            var stream = dubLanguage == null
                ? await liveStreams
                    .GetReplayStream(Session, ChatId, StartAt, RewindOffset, Speed, cancellationToken)
                    .ConfigureAwait(false)
                : await liveStreams
                    .GetReplayStream(Session, ChatId, StartAt, RewindOffset, Speed, dubLanguage, cancellationToken)
                    .ConfigureAwait(false);
            Log.LogInformation("<- LiveStreams.GetReplayStream({ChatId})", ChatId);
            Tracer.Point("Stream: GetReplayStream returned");

            var items = Tracer.IsEnabled ? TraceItems(stream, cancellationToken) : stream;
            var demuxer = new AudioStreamDemuxer(items, demuxerLog, cancellationToken.CreateLinkedTokenSource());
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

    // Private methods

    private async IAsyncEnumerable<MuxedAudioStreamItem> TraceItems(
        IAsyncEnumerable<MuxedAudioStreamItem> items,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var isFirstItem = true;
        var trackTracers = new Dictionary<int, Tracer>();
        var framedStreamIndexes = new HashSet<int>();
        await foreach (var item in items.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (isFirstItem) {
                isFirstItem = false;
                Tracer.Point("Stream: first item received");
            }
            switch (item) {
            case MuxedAudioStreamStart start:
                trackTracers[start.StreamIndex] = GetTrackTracer(Tracer, start.StreamInfo);
                break;
            case MuxedAudioFrame frame when framedStreamIndexes.Add(frame.StreamIndex):
                trackTracers.GetValueOrDefault(frame.StreamIndex, Tracer)
                    .Point($"first frame received, offset {frame.Offset.TotalSeconds:F3}s");
                break;
            case MuxedAudioStreamEnd end:
                trackTracers.GetValueOrDefault(end.StreamIndex, Tracer).Point("StreamEnd received");
                break;
            }
            yield return item;
        }
        Tracer.Point("Stream: completed");
    }
}
