namespace ActualChat.Testing;

public static class ComputedTestExt
{
    public static async Task WhenMet(
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
        var c = await anonymousComputedSource.When(x => x, cts.Token).ConfigureAwait(false);
        if (!c.Value)
            await condition.Invoke(CancellationToken.None); // Should throw
    }
}
