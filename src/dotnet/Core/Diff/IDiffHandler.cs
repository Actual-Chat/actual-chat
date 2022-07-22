namespace ActualChat.Diff;

public interface IDiffHandler
{
    DiffEngine Engine { get; }

    object Diff(object? source, object? target);
    object? Patch(object? source, object diff);
}

public interface IDiffHandler<T, TDiff> : IDiffHandler
{
    TDiff Diff(T source, T target);
    T Patch(T source, TDiff diff);
}
