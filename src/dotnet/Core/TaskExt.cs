namespace ActualChat;

public static class TaskExt
{
    public static Task Suppress<TException>(this Task task)
        where TException : Exception
    {
#pragma warning disable VSTHRD003
        if (task.IsCompletedSuccessfully || task.IsCanceled)
            return task;
        return WrapToSuppress(task);
#pragma warning restore VSTHRD003

        static async Task WrapToSuppress(Task task1) {
            try {
                await task1.ConfigureAwait(false);
            }
            catch (TException) { }
        }
    }

    public static Task<T> Suppress<T, TException>(this Task<T> task)
        where TException : Exception
    {
#pragma warning disable VSTHRD003
        if (task.IsCompletedSuccessfully || task.IsCanceled)
            return task;
        return WrapToSuppress(task);
#pragma warning restore VSTHRD003

        static async Task<T> WrapToSuppress(Task<T> task1) {
            try {
                return await task1.ConfigureAwait(false);
            }
            catch (TException) {
                return default!;
            }
        }
    }

    public static ValueTask Suppress<TException>(this ValueTask task)
        where TException : Exception
    {
        if (task.IsCompletedSuccessfully || task.IsCanceled)
            return task;
        return WrapToSuppress(task);

        static async ValueTask WrapToSuppress(ValueTask task1) {
            try {
                await task1.ConfigureAwait(false);
            }
            catch (TException) { }
        }
    }

    public static ValueTask<T> Suppress<T, TException>(this ValueTask<T> task)
        where TException : Exception
    {
        if (task.IsCompletedSuccessfully || task.IsCanceled)
            return task;
        return WrapToSuppress(task);

        static async ValueTask<T> WrapToSuppress(ValueTask<T> task1) {
            try {
                return await task1.ConfigureAwait(false);
            }
            catch (TException) {
                return default!;
            }
        }
    }
}
