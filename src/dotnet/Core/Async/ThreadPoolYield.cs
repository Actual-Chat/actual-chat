namespace ActualChat;

/// <summary>
/// <c>await ThreadPoolYield.Yield()</c> continues on a thread-pool thread, unconditionally:
/// unlike <see cref="Task.Yield"/> it never posts back to the current synchronization context,
/// so it's the way off the Blazor dispatcher once a method has read what it needs there.
/// </summary>
public static class ThreadPoolYield
{
    public static Awaitable Yield() => default;

    // Nested types

    public readonly struct Awaitable
    {
        public Awaiter GetAwaiter() => default;
    }

    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        public bool IsCompleted => false;
        public void GetResult() { }

        public void OnCompleted(Action continuation)
            => ThreadPool.QueueUserWorkItem(static state => ((Action)state!)(), continuation);

        public void UnsafeOnCompleted(Action continuation)
            => ThreadPool.UnsafeQueueUserWorkItem(static state => ((Action)state!)(), continuation);
    }
}
