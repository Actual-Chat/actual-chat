namespace ActualChat;

public static class BackgroundTask
{
    [UnconditionalSuppressMessage("Tasks", "MA0100:AwaitBeforeDisposing", Justification = "TrySuppressFlow' dispose doesn't need to be awaited.")]
    public static Task Run(
        Func<Task> taskFactory,
        CancellationToken cancellationToken = default)
    {
        using var _ = ExecutionContextExt.TrySuppressFlow();
        try {
            return Task.Run(taskFactory, cancellationToken);
        }
        catch (OperationCanceledException) {
            return Task.FromCanceled(cancellationToken);
        }
        catch (Exception e) {
            return Task.FromException(e);
        }
    }

    [UnconditionalSuppressMessage("Tasks", "MA0100:AwaitBeforeDisposing", Justification = "TrySuppressFlow' dispose doesn't need to be awaited.")]
    public static Task<T> Run<T>(
        Func<Task<T>> taskFactory,
        CancellationToken cancellationToken = default)
    {
        using var _ = ExecutionContextExt.TrySuppressFlow();
        try {
            return Task.Run(taskFactory, cancellationToken);
        }
        catch (OperationCanceledException) {
            return Task.FromCanceled<T>(cancellationToken);
        }
        catch (Exception e) {
            return Task.FromException<T>(e);
        }
    }

    [UnconditionalSuppressMessage("Tasks", "MA0100:AwaitBeforeDisposing", Justification = "TrySuppressFlow' dispose doesn't need to be awaited.")]
    public static Task Run(
        Func<Task> taskFactory,
        Action<Exception> errorHandler,
        CancellationToken cancellationToken = default)
    {
        using var _ = ExecutionContextExt.TrySuppressFlow();
        try {
            return Task.Run(taskFactory, cancellationToken).WithErrorHandler(errorHandler, cancellationToken);
        }
        catch (OperationCanceledException) {
            return Task.FromCanceled(cancellationToken);
        }
        catch (Exception e) {
            errorHandler.Invoke(e);
            return Task.FromException(e);
        }
    }

    [UnconditionalSuppressMessage("Tasks", "MA0100:AwaitBeforeDisposing", Justification = "TrySuppressFlow' dispose doesn't need to be awaited.")]
    public static Task<T> Run<T>(
        Func<Task<T>> taskFactory,
        Action<Exception> errorHandler,
        CancellationToken cancellationToken = default)
    {
        using var _ = ExecutionContextExt.TrySuppressFlow();
        try {
            return Task.Run(taskFactory, cancellationToken).WithErrorHandler(errorHandler, cancellationToken);
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
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => Run(taskFactory, e => errorLog.LogError(e, message), cancellationToken);

    public static Task<T> Run<T>(
        Func<Task<T>> taskFactory,
        ILogger errorLog,
        string message,
        CancellationToken cancellationToken = default)
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => Run(taskFactory, e => errorLog.LogError(e, message), cancellationToken);
}
