namespace ActualChat;

/// <summary>
/// <c>await ThreadPoolExt.Yield()</c> continues on a thread-pool thread, unconditionally:
/// unlike <see cref="Task.Yield"/> it never posts back to the current synchronization context,
/// so it's the way off the Blazor dispatcher once a method has read what it needs there.
/// </summary>
public static class ThreadPoolExt
{
    public static ThreadPoolYieldAwaitable Yield() => default;

    // Nested types

    public readonly struct ThreadPoolYieldAwaitable
    {
        public ThreadPoolYieldAwaiter GetAwaiter() => default;
    }

    public readonly struct ThreadPoolYieldAwaiter : ICriticalNotifyCompletion
    {
        public bool IsCompleted => false;
        public void GetResult() { }

        public void OnCompleted(Action continuation)
            => ThreadPool.QueueUserWorkItem(static state => ((Action)state!).Invoke(), continuation);

        public void UnsafeOnCompleted(Action continuation)
            => ThreadPool.UnsafeQueueUserWorkItem(static state => ((Action)state!).Invoke(), continuation);
    }
}
