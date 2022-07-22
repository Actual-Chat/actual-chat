namespace ActualChat.Diff.Handlers;

public abstract class DiffHandlerBase<T, TDiff> : IDiffHandler<T, TDiff>
{
    public DiffEngine Engine { get; }

    protected DiffHandlerBase(DiffEngine engine)
        => Engine = engine;

    public object Diff(object? source, object? target)
        // ReSharper disable once HeapView.PossibleBoxingAllocation
        => Diff((T) source!, (T) target!)!;

    public object? Patch(object? source, object diff)
        // ReSharper disable once HeapView.PossibleBoxingAllocation
        => Patch((T) source!, (TDiff) diff);

    public abstract TDiff Diff(T source, T target);
    public abstract T Patch(T source, TDiff diff);
}
