namespace ActualChat.Testing;

public static class TestsExt
{
    public static async Task<T> When<T>(Func<Task<T>> condition, TimeSpan? waitDuration = null)
    {
        T result = default!;
        await TestExt.When(async () => {
                result = await condition();
            },
            null,
            waitDuration ?? TimeSpan.FromSeconds(10));
        return result;
    }
    public static async Task<T> When<T>(Func<Task<T>> condition, IEnumerable<TimeSpan> checkIntervals, TimeSpan? waitDuration = null)
    {
        T result = default!;
        await TestExt.When(async () => {
                result = await condition();
            },
            checkIntervals,
            waitDuration ?? TimeSpan.FromSeconds(10));
        return result;
    }
}
