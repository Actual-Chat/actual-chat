namespace ActualChat.Testing;

public static class ComputedTestExt
{
    public static readonly TimeSpan DefaultWaitDuration = TimeSpan.FromSeconds(5);

    public static async Task When(
        IServiceProvider services,
        Func<CancellationToken, Task> condition,
        TimeSpan? waitDuration = null)
    {
        var anonymousComputedSource = new AnonymousComputedSource<bool>(services,
            async (_, ct) => {
                try {
                    await condition.Invoke(ct).ConfigureAwait(false);
                    return true;
                }
                catch (Exception e) when (!e.IsCancellationOf(ct)) {
                    return false;
                }
            });

        using var cts = new CancellationTokenSource(waitDuration ?? DefaultWaitDuration);
        await anonymousComputedSource.When(x => x, cts.Token).ConfigureAwait(false);
        await condition.Invoke(CancellationToken.None); // Should throw or pass
    }

    public static async Task<T> When<T>(
        IServiceProvider services,
        Func<CancellationToken, Task<T>> condition,
        TimeSpan? waitDuration = null)
    {
        var anonymousComputedSource = new AnonymousComputedSource<bool>(services,
            async (_, ct) => {
                try {
                    await condition.Invoke(ct).ConfigureAwait(false);
                    return true;
                }
                catch (Exception e) when (!e.IsCancellationOf(ct)) {
                    return false;
                }
            });

        using var cts = new CancellationTokenSource(waitDuration ?? DefaultWaitDuration);
        await anonymousComputedSource.When(x => x, cts.Token).ConfigureAwait(false);
        return await condition.Invoke(CancellationToken.None); // Should throw or pass
    }
}
