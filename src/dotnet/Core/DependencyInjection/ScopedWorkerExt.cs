namespace ActualChat.DependencyInjection;

public static class ScopedWorkerExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TScopedWorker Start<TScopedWorker>(this TScopedWorker worker)
        where TScopedWorker : IScopedWorker
    {
        _ = worker.Run();
        return worker;
    }
}
