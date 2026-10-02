namespace ActualChat.Core.UnitTests.Channels;

public static class AsyncMemoizerTestExt
{
    private static readonly TimeSpan BufferTimeout = TimeSpan.FromSeconds(5).CiScaled();

    public static async Task WhenBuffered<T>(this IAsyncMemoizer<T> memoizer, int expectedCount)
    {
        // Spins instead of TestWait.WhenPolled: the first check almost always runs before the memoizer's
        // Read task does, and the race tests wait on each of up to 50K iterations, so 50 ms per poll adds
        // up to hours. The count is read once per pass and checked before the deadline, so a starved loop
        // still sees items that arrived meanwhile, and the timeout reports the count it decided on.
        var startedAt = CpuTimestamp.Now;
        var count = memoizer.BufferedCount;
        while (count < expectedCount) {
            if (startedAt.Elapsed > BufferTimeout)
                throw new TimeoutException($"Timed out waiting for {expectedCount} buffered items, got {count}");

            await Task.Yield();
            count = memoizer.BufferedCount;
        }
    }
}
