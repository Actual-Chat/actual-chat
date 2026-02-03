using ActualChat.Rtc;

namespace ActualChat.UI.Blazor.App.Services.Rtc;

/// <summary>
/// Manages RTC stream connection with automatic reconnection on disconnect.
/// </summary>
public sealed class RtcStreamProcessor : WorkerBase
{
    private static bool DebugMode => Constants.DebugMode.RtcStreaming;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(0.25);

    private ILogger Log { get; }
    private ILogger? DebugLog { get; }

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public ChatId ChatId { get; }
    public RtcStreamingSettings Settings { get; }

    public event Action<RtcStreamInfo, IAsyncEnumerable<byte[]>>? StreamStarted;

    public RtcStreamProcessor(IServiceProvider services,
        Session session,
        ChatId chatId,
        RtcStreamingSettings settings,
        CancellationTokenSource? stopTokenSource = null) : base(stopTokenSource)
    {
        Services = services;
        Session = session;
        ChatId = chatId;
        Settings = settings;
        Log = services.LogFor(GetType());
        DebugLog = DebugMode ? Log : null;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var rtcHub = Services.GetRequiredService<IRtcHub>();
        var demuxerLog = Services.LogFor<RtcStreamDemuxer>();
        while (!cancellationToken.IsCancellationRequested) {
            try {
                DebugLog?.LogInformation("-> RtcHub.GetStream({ChatId})", ChatId);
                var stream = await rtcHub.GetStream(Session, ChatId, Settings, cancellationToken).ConfigureAwait(false);
                DebugLog?.LogInformation("<- RtcHub.GetStream({ChatId})", ChatId);

                var demuxer = new RtcStreamDemuxer(stream, demuxerLog, cancellationToken.CreateLinkedTokenSource());
                await using var _ = demuxer.ConfigureAwait(false);
                demuxer.StreamStarted += (info, frames) => StreamStarted?.Invoke(info, frames);

                DebugLog?.LogInformation("Demuxing RTC stream for {ChatId}...", ChatId);
                await demuxer.Run().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed for chat {ChatId}, will reconnect in {Delay}",
                    ChatId, ReconnectDelay.ToShortString());
            }

            // Wait before reconnecting
            await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
