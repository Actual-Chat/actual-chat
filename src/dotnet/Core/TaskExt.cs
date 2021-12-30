namespace ActualChat;

public static class TaskExt
{
    private static readonly UncompletedTaskException UncompletedTaskError = new();

    public static Result<T> ToResult<T>(this Task<T> task)
    {
        if (!task.IsCompleted)
            return new Result<T>(default!, UncompletedTaskError);
#pragma warning disable VSTHRD002
        return task.IsCompletedSuccessfully
            ? new Result<T>(task.Result, null)
            : new Result<T>(default!, task.Exception);
#pragma warning restore VSTHRD002
    }

    public static Result<T> ToResult<T>(this ValueTask<T> task)
    {
        if (!task.IsCompleted)
            return new Result<T>(default!, UncompletedTaskError);
#pragma warning disable VSTHRD002
        return task.IsCompletedSuccessfully
            ? new Result<T>(task.Result, null)
            : new Result<T>(default!, task.AsTask().Exception);
#pragma warning restore VSTHRD002
    }

    // Suppress

    public static Task Suppress<TException>(this Task task)
        where TException : Exception
    {
#pragma warning disable VSTHRD003
        if (task.IsCompletedSuccessfully || task.IsCanceled)
            return task;
        return WrapToSuppress(task);
#pragma warning restore VSTHRD003

        static async Task WrapToSuppress(Task task) {
            try {
                await task.ConfigureAwait(false);
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

        static async Task<T> WrapToSuppress(Task<T> task) {
            try {
                return await task.ConfigureAwait(false);
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

        static async ValueTask WrapToSuppress(ValueTask task) {
            try {
                await task.ConfigureAwait(false);
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

        static async ValueTask<T> WrapToSuppress(ValueTask<T> task) {
            try {
                return await task.ConfigureAwait(false);
            }
            catch (TException) {
                return default!;
            }
        }
    }

    // WithErrorHandler

    public static async Task WithErrorHandler(this Task task, Action<Exception> errorHandler)
    {
        try {
            await task.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            errorHandler(e);
            throw;
        }
    }

    public static async Task<T> WithErrorHandler<T>(this Task<T> task, Action<Exception> errorHandler)
    {
        try {
            return await task.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            errorHandler(e);
            throw;
        }
    }

    // WithErrorLog

    public static Task WithErrorLog(this Task task, ILogger errorLog, string message)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => task.WithErrorHandler(e => errorLog.LogError(e, message));

    public static Task<T> WithErrorLog<T>(this Task<T> task, ILogger errorLog, string message)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => task.WithErrorHandler(e => errorLog.LogError(e, message));
}
