namespace ActualChat.UI.Blazor.Services;

public interface IHistoryStepRefImpl
{
    void MarkClosed();
}

public sealed class HistoryStepRef : IHasId<Symbol>, IHistoryStepRefImpl
{
    private static long _lastId;

    private readonly TaskCompletionSource _whenClosedSource = TaskCompletionSourceExt.New();

    public Symbol Id { get; }
    public string Descriptor { get; }
    public HistoryStepper Stepper { get; }

    public Task WhenClosed => _whenClosedSource.Task;

    public HistoryStepRef(string descriptor, HistoryStepper stepper)
    {
        Id = $"Step-{descriptor}-{Interlocked.Increment(ref _lastId)}";
        Descriptor = descriptor;
        Stepper = stepper;
    }

    public void Close()
        => Stepper.Close(Id);

    void IHistoryStepRefImpl.MarkClosed()
        => _whenClosedSource.TrySetResult();
}
