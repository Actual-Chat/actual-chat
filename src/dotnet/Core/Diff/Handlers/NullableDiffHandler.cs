namespace ActualChat.Diff.Handlers;

public class NullableDiffHandler<T> : DiffHandlerBase<T, T?>
    where T : struct
{
    public NullableDiffHandler(DiffEngine engine) : base(engine) { }

    public override T? Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? null : target;

    public override T Patch(T source, T? diff)
        => diff ?? source;
}
