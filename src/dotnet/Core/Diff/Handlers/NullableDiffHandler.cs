namespace ActualChat.Diff.Handlers;

/// <summary>
/// Handles diffs for value types using nullable as the diff representation.
/// </summary>
public sealed class NullableDiffHandler<T>(DiffEngine engine) : DiffHandlerBase<T, T?>(engine)
    where T : struct
{
    public override T? Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? null : target;

    public override T Patch(T source, T? diff)
        => diff ?? source;
}
