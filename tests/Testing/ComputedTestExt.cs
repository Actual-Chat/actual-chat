namespace ActualChat.Testing;

public static class ComputedTestExt
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task When(
        IServiceProvider services,
        Func<CancellationToken, Task> condition,
        TimeSpan? timeout = null)
    {
        var computedSource = new AnonymousComputedSource<bool>(services,
            async (_, ct) => {
                try {
                    await condition.Invoke(ct).ConfigureAwait(false);
                    return true;
                }
                catch (Exception e) when (!e.IsCancellationOf(ct)) {
                    return false;
                }
            });
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        await computedSource.When(x => x, timeoutCts.Token).ConfigureAwait(false);
        await condition.Invoke(CancellationToken.None); // Should throw or pass
    }

    public static async Task<T> When<T>(
        IServiceProvider services,
        Func<CancellationToken, Task<T>> condition,
        TimeSpan? timeout = null)
    {
        var computedSource = new AnonymousComputedSource<bool>(services,
            async (_, ct) => {
                try {
                    await condition.Invoke(ct).ConfigureAwait(false);
                    return true;
                }
                catch (Exception e) when (!e.IsCancellationOf(ct)) {
                    return false;
                }
            });
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        await computedSource.When(x => x, timeoutCts.Token).ConfigureAwait(false);
        return await condition.Invoke(CancellationToken.None); // Should throw or pass
    }
}
