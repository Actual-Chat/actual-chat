namespace ActualChat.Messaging;

public interface IMessageProcess
{
    object UntypedMessage { get; }
    CancellationToken CancellationToken { get; }
    Task WhenStarted { get; }
    Task<object?> WhenCompleted { get; }

    void MarkStarted();
    void MarkCompleted(Result<object?> result);
    void MarkCompletedAfter(Task<object?> resultTask);
    void MarkFailed(Exception error);
}

public interface IMessageProcess<out TMessage> : IMessageProcess
{
    public TMessage Message { get; }
}

public abstract class MessageProcess : IMessageProcess
{
    private static readonly ConcurrentDictionary<
        Type,
        Func<object, CancellationToken, TaskCompletionSource?, TaskCompletionSource<object?>?, object>>
        MessageProcessorCtorCache = new();

    protected TaskCompletionSource WhenStartedSource { get; init; } = null!;
    protected TaskCompletionSource<object?> WhenCompletedSource { get; init; } = null!;

    public abstract object UntypedMessage { get; }
    public CancellationToken CancellationToken { get; protected init; }
    public Task WhenStarted => WhenStartedSource.Task;
    public Task<object?> WhenCompleted => WhenCompletedSource.Task;

    public abstract void MarkStarted();
    public abstract void MarkCompleted(Result<object?> result);
    public abstract void MarkCompletedAfter(Task<object?> resultTask);
    public abstract void MarkFailed(Exception error);

    public static IMessageProcess New(
        object message,
        CancellationToken cancellationToken,
        TaskCompletionSource? whenStarted = null,
        TaskCompletionSource<object?>? whenCompleted = null)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var ctor = MessageProcessorCtorCache.GetOrAdd(
            message.GetType(),
            t => (Func<object, CancellationToken, TaskCompletionSource?, TaskCompletionSource<object?>?, object>)
                typeof(MessageProcess<>)
                    .MakeGenericType(t)
                    .GetConstructorDelegate(
                        typeof(object),
                        typeof(CancellationToken),
                        typeof(TaskCompletionSource),
                        typeof(TaskCompletionSource<object>))!);
        return (IMessageProcess)ctor.Invoke(message, cancellationToken, whenStarted, whenCompleted);
    }
}

public class MessageProcess<TMessage> : MessageProcess, IMessageProcess<TMessage>
    where TMessage : class
{
    public TMessage Message { get; }
    public override object UntypedMessage => Message;

    public MessageProcess(
        object message,
        CancellationToken cancellationToken,
        TaskCompletionSource? whenStartedSource = null,
        TaskCompletionSource<object?>? whenCompletedSource = null)
    {
        Message = (TMessage)message;
        CancellationToken = cancellationToken;
        WhenStartedSource = whenStartedSource ?? TaskCompletionSourceExt.New();
        WhenCompletedSource = whenCompletedSource ?? TaskCompletionSourceExt.New<object?>();
    }

    public MessageProcess(
        TMessage message,
        CancellationToken cancellationToken,
        TaskCompletionSource? whenStartedSource = null,
        TaskCompletionSource<object?>? whenCompletedSource = null)
    {
        Message = message;
        CancellationToken = cancellationToken;
        WhenStartedSource = whenStartedSource ?? TaskCompletionSourceExt.New();
        WhenCompletedSource = whenCompletedSource ?? TaskCompletionSourceExt.New<object?>();
    }

    public override void MarkStarted()
    {
        DefaultLog.LogDebug(nameof(MessageProcess<TMessage>) + "." + nameof(MarkStarted));

        WhenStartedSource.TrySetResult();
    }

    public override void MarkCompleted(Result<object?> result)
    {
        DefaultLog.LogDebug(nameof(MessageProcess<TMessage>) + "." + nameof(MarkCompleted) + " {Result}", result.ValueOrDefault);

        WhenStartedSource.TrySetResult();
        WhenCompletedSource.TrySetFromResult(result, CancellationToken);
    }

    public override void MarkCompletedAfter(Task<object?> resultTask)
        => _ = resultTask.ContinueWith(async t => {
            try {
                var result = await t.ConfigureAwait(false);
                MarkCompleted(result);
            }
            catch (Exception e) {
                MarkFailed(e);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public override void MarkFailed(Exception error)
    {
        DefaultLog.LogDebug(nameof(MessageProcess<TMessage>) + "." + nameof(MarkFailed));

        if (error is OperationCanceledException && CancellationToken.IsCancellationRequested) {
            WhenStartedSource.TrySetCanceled(CancellationToken);
            WhenCompletedSource.TrySetCanceled(CancellationToken);
        }
        else {
            WhenStartedSource.TrySetException(error);
            WhenCompletedSource.TrySetException(error);
        }
    }
}
