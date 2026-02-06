namespace ActualChat.Diff;

/// <summary>
/// Handles computing diffs and applying patches between objects.
/// </summary>
public interface IDiffHandler
{
    DiffEngine Engine { get; }

    object Diff(object? source, object? target);
    object? Patch(object? source, object diff);
}

/// <summary>
/// Strongly-typed diff handler for computing and applying diffs.
/// </summary>
public interface IDiffHandler<T, TDiff> : IDiffHandler
{
    TDiff Diff(T source, T target);
    T Patch(T source, TDiff diff);
}
