namespace ActualChat;

#pragma warning disable CA2000

public sealed class FuncWorker : WorkerBase
{
    private Func<CancellationToken, Task> TaskFactory { get; }

    public static FuncWorker New(Func<CancellationToken, Task> taskFactory)
        => new(taskFactory, null);
    public static FuncWorker New(Func<CancellationToken, Task> taskFactory, CancellationToken cancellationToken)
        => new(taskFactory, cancellationToken != default ? cancellationToken.CreateLinkedTokenSource() : null);
    public static FuncWorker New(Func<CancellationToken, Task> taskFactory, CancellationTokenSource? cancellationTokenSource)
        => new(taskFactory, cancellationTokenSource);

    public static FuncWorker Start(Func<CancellationToken, Task> taskFactory)
        => Start(taskFactory, null);
    public static FuncWorker Start(Func<CancellationToken, Task> taskFactory, CancellationToken cancellationToken)
        => Start(taskFactory, cancellationToken != default ? cancellationToken.CreateLinkedTokenSource() : null);
    public static FuncWorker Start(Func<CancellationToken, Task> taskFactory, CancellationTokenSource? cancellationTokenSource)
    {
        var worker = New(taskFactory, cancellationTokenSource);
        worker.Start();
        return worker;
    }

    private FuncWorker(Func<CancellationToken, Task> taskFactory, CancellationTokenSource? cancellationTokenSource)
        : base(cancellationTokenSource)
        => TaskFactory = taskFactory;

    protected override async Task OnRun(CancellationToken cancellationToken)
        => await TaskFactory(cancellationToken).ConfigureAwait(false);
}
