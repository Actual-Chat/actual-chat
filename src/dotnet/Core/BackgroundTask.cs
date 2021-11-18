namespace ActualChat;

public static class BackgroundTask
{
    public static Task Run(
        Func<Task> taskFactory,
        Action<Exception> errorHandler,
        CancellationToken cancellationToken = default)
    {
        try {
            return Task.Run(taskFactory, cancellationToken)
                .WithErrorHandler(errorHandler);
        }
        catch (OperationCanceledException) {
            return Task.FromCanceled(cancellationToken);
        }
        catch (Exception e) {
            errorHandler.Invoke(e);
            return Task.FromException(e);
        }
    }

    public static Task<T> Run<T>(
        Func<Task<T>> taskFactory,
        Action<Exception> errorHandler,
        CancellationToken cancellationToken = default)
    {
        try {
            return Task.Run(taskFactory, cancellationToken)
                .WithErrorHandler(errorHandler);
        }
        catch (OperationCanceledException) {
            return Task.FromCanceled<T>(cancellationToken);
        }
        catch (Exception e) {
            errorHandler.Invoke(e);
            return Task.FromException<T>(e);
        }
    }

    public static Task Run(
        Func<Task> taskFactory,
        ILogger errorLog,
        string message,
        CancellationToken cancellationToken = default)
        => Run(taskFactory, e => errorLog.LogError(e, message), cancellationToken);

    public static Task<T> Run<T>(
        Func<Task<T>> taskFactory,
        ILogger errorLog,
        string message,
        CancellationToken cancellationToken = default)
        => Run(taskFactory, e => errorLog.LogError(e, message), cancellationToken);
}
