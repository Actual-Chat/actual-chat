namespace ActualChat.Diff.Handlers;

public class NullableDiffHandler<T>(DiffEngine engine) : DiffHandlerBase<T, T?>(engine)
    where T : struct
{
    public override T? Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? null : target;

    public override T Patch(T source, T? diff)
        => diff ?? source;
}
