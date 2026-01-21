namespace ActualChat;

public class DelegatingWorker<TResult>(
    Func<CancellationToken, Task<TResult>> jobFactory,
    CancellationTokenSource? stopTokenSource = null
    ) : WorkerBase(stopTokenSource)
{
    public TResult? Result { get; private set; }

    protected override async Task OnRun(CancellationToken cancellationToken)
        => Result = await jobFactory(cancellationToken).ConfigureAwait(false);

    public new async Task<TResult> Run()
    {
 #pragma warning disable MA0040
        await base.Run().ConfigureAwait(false);
 #pragma warning restore MA0040
        return Result!;
    }
}

public sealed class DelegatingWorker(
    Func<CancellationToken, Task> jobFactory,
    CancellationTokenSource? stopTokenSource = null)
    : DelegatingWorker<Unit>(jobFactory.ToUnitTaskFactory(), stopTokenSource)
{
    public static DelegatingWorker<TResult> New<TResult>(
        Func<CancellationToken, Task<TResult>> jobFactory,
        CancellationTokenSource? stopTokenSource = null,
        bool start = true)
    {
        var worker = new DelegatingWorker<TResult>(jobFactory, stopTokenSource);
        if (start)
            worker.Start();
        return worker;
    }

    public static DelegatingWorker New(
        Func<CancellationToken, Task> jobFactory,
        CancellationTokenSource? stopTokenSource = null,
        bool start = true)
    {
        var worker = new DelegatingWorker(jobFactory.ToUnitTaskFactory(), stopTokenSource);
        if (start)
            worker.Start();
        return worker;
    }
}
