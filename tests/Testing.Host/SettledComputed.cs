namespace ActualChat.Testing.Host;

/// <summary>
/// Captures a <see cref="Computed{T}"/> that stopped getting invalidated - the starting point a test
/// asserting "this act must NOT invalidate X" needs.
/// </summary>
public static class SettledComputed
{
    public static async Task<Computed<T>> Capture<T>(
        Func<Task<T>> producer,
        TimeSpan? quietPeriod = null,
        TimeSpan? timeout = null)
    {
        // Background flows keep writing for a while after a chat or a live session is created, and their
        // invalidations are indistinguishable from the ones the act under test produces.
        var quiet = quietPeriod ?? TimeSpan.FromSeconds(1);
        var startedAt = CpuTimestamp.Now;
        while (true) {
            var computed = await Computed.Capture(producer).ConfigureAwait(false);
            var whenInvalidated = computed.WhenInvalidated(CancellationToken.None);
            var completed = await Task.WhenAny(whenInvalidated, Task.Delay(quiet)).ConfigureAwait(false);
            if (completed != whenInvalidated)
                return computed;
            if (startedAt.Elapsed > (timeout ?? TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The captured computed keeps getting invalidated.");
        }
    }
}
