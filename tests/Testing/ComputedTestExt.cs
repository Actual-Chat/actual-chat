namespace ActualChat.Testing;

public static class ComputedTestExt
{
    public static async Task When(
        IServiceProvider services,
        Func<CancellationToken, Task> condition,
        TimeSpan waitDuration)
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

        using var cts = new CancellationTokenSource(waitDuration);
        await anonymousComputedSource.When(x => x, cts.Token).ConfigureAwait(false);
        await condition.Invoke(CancellationToken.None); // Should throw or pass
    }

    public static async Task<T> When<T>(
        IServiceProvider services,
        Func<CancellationToken, Task<T>> condition,
        TimeSpan waitDuration)
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

        using var cts = new CancellationTokenSource(waitDuration);
        await anonymousComputedSource.When(x => x, cts.Token).ConfigureAwait(false);
        return await condition.Invoke(CancellationToken.None); // Should throw or pass
    }
}
