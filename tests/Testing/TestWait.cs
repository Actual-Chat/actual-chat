namespace ActualChat.Testing;

/// <summary>
/// The tests' one way to wait for a condition: <see cref="When(Func{CancellationToken, Task}, TimeSpan?, bool)"/>
/// re-runs its assertion on invalidation, <see cref="WhenPolled(Action, TimeSpan?, bool)"/> re-runs it on a timer.
/// Every budget passes through here, so the build-agent scale is applied in one place.
/// </summary>
public static class TestWait
{
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public static Task When(
        Func<CancellationToken, Task> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
        => ComputedTest.When(assertion, Budget(timeout, isExactTimeout));

    public static Task<T> When<T>(
        Func<CancellationToken, Task<T>> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
        => ComputedTest.When(assertion, Budget(timeout, isExactTimeout));

    public static Task When(
        IServiceProvider services,
        Func<CancellationToken, Task> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
        => ComputedTest.When(services, assertion, Budget(timeout, isExactTimeout));

    public static Task WhenPolled(
        Action assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
        => TestExt.When(assertion, null, Budget(timeout, isExactTimeout));

    public static Task WhenPolled(
        Action assertion,
        IEnumerable<TimeSpan> checkIntervals,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
        => TestExt.When(assertion, checkIntervals, Budget(timeout, isExactTimeout));

    public static Task WhenPolled(
        Func<Task> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
        => TestExt.When(assertion, null, Budget(timeout, isExactTimeout));

    public static Task WhenPolled(
        Func<Task> assertion,
        IEnumerable<TimeSpan> checkIntervals,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
        => TestExt.When(assertion, checkIntervals, Budget(timeout, isExactTimeout));

    // The type argument has to be explicit at the call site: without it the non-generic overload
    // above wins the tie-break and the lambda's value has nowhere to go.
    public static Task<T> WhenPolled<T>(
        Func<Task<T>> assertion,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
        => WhenPolled(assertion, null!, timeout, isExactTimeout);

    public static async Task<T> WhenPolled<T>(
        Func<Task<T>> assertion,
        IEnumerable<TimeSpan> checkIntervals,
        TimeSpan? timeout = null,
        bool isExactTimeout = false)
    {
        var result = default(T)!;
        await TestExt.When(async () => {
                result = await assertion.Invoke();
            },
            checkIntervals,
            Budget(timeout, isExactTimeout));
        return result;
    }

    // Private methods

    private static TimeSpan Budget(TimeSpan? timeout, bool isExactTimeout)
        => timeout is not { } value ? DefaultTimeout.CiScaled()
            : isExactTimeout ? value
            : value.CiScaled();
}
