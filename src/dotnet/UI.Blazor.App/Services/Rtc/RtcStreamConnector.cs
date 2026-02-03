using ActualChat.Rtc;

namespace ActualChat.UI.Blazor.App.Services.Rtc;

/// <summary>
/// Manages RTC stream connection with automatic reconnection on disconnect.
/// </summary>
public sealed class RtcStreamConnector(
    IServiceProvider services,
    Session session,
    ChatId chatId,
    RtcStreamingSettings settings)
    : WorkerBase
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(0.25);

    public event Action<RtcStreamStartedArgs>? StreamStarted;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var log = services.LogFor(GetType());
        var rtcHub = services.GetRequiredService<IRtcHub>();
        while (!cancellationToken.IsCancellationRequested) {
            try {
                log.LogInformation("Connecting to RTC Hub for chat {ChatId}", chatId);
                var rtcStream = await rtcHub.GetStream(session, chatId, settings, cancellationToken).ConfigureAwait(false);
                log.LogInformation("Connected to RTC Hub for chat {ChatId}", chatId);

                var demuxer = new RtcStreamDemuxer(rtcStream, log);
                await using var _ = demuxer.ConfigureAwait(false);

                demuxer.StreamStarted += args => StreamStarted?.Invoke(args);

                log.LogInformation("Starting demuxer for chat {ChatId}", chatId);
                await demuxer.Run().ConfigureAwait(false);
                log.LogInformation("Demuxer stopped for chat {ChatId}", chatId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) {
                log.LogWarning(e, "RTC stream error for chat {ChatId}, reconnecting in {Delay}s",
                    chatId, ReconnectDelay.TotalSeconds);
            }

            // Wait before reconnecting
            await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
            log.LogInformation("Reconnecting to RTC Hub for chat {ChatId}", chatId);
        }
    }
}
