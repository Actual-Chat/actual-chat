namespace ActualChat.UI.Blazor;

public static class ScopedWorkerExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TScopedWorker Start<TScopedWorker>(this TScopedWorker worker)
        where TScopedWorker : IUIWorker
    {
        _ = worker.Run();
        return worker;
    }
}
