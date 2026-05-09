namespace ActualChat.Streaming.Services;

/// <summary>
/// Interval-based "is the stream still alive?" watchdog. Wakes every
/// <c>interval</c>, samples-and-resets the producer's frame counter, and
/// cancels the supplied <see cref="CancellationTokenSource"/> after
/// <c>maxConsecutiveZeroIntervals</c> ticks with zero arrivals.
/// </summary>
/// <remarks>
/// Designed for the "browser tab closed without a clean WebSocket close"
/// scenario where a per-frame <c>CancelAfter</c> reset would either
/// timer-storm or miss the dead-stream signal entirely. Counter is
/// supplied via a callback so the caller controls whatever atomic
/// primitive fits its hot path.
/// </remarks>
public static class StreamSilenceWatchdog
{
    public static async Task Run(
        Func<int> consumeFrameCount,
        CancellationTokenSource onSilenceCts,
        TimeSpan interval,
        int maxConsecutiveZeroIntervals,
        Action<int>? onSilenceDetected,
        CancellationToken cancellationToken)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Must be positive.");
        if (maxConsecutiveZeroIntervals < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConsecutiveZeroIntervals), maxConsecutiveZeroIntervals, "Must be ≥ 1.");

        var consecutiveZero = 0;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                var count = consumeFrameCount();
                if (count == 0) {
                    consecutiveZero++;
                    if (consecutiveZero >= maxConsecutiveZeroIntervals) {
                        onSilenceDetected?.Invoke(consecutiveZero);
                        try {
                            await onSilenceCts.CancelAsync().ConfigureAwait(false);
                        }
                        catch { /* already disposed/cancelled */ }
                        return;
                    }
                }
                else {
                    consecutiveZero = 0;
                }
            }
        }
        catch (OperationCanceledException) {
            // Parent token cancelled — normal teardown, not a silence event.
        }
    }
}
