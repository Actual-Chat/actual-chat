namespace ActualChat.Diff.Handlers;

public class CloneDiffHandler<T> : DiffHandlerBase<T, T>
{
    private readonly bool _isClass = typeof(T).IsClass;

    public CloneDiffHandler(DiffEngine engine) : base(engine) { }

    public override T Diff(T source, T target)
    {
        if (!_isClass)
            return target;

        return EqualityComparer<T>.Default.Equals(source, target) ? default! : target;
    }

    public override T Patch(T source, T diff)
    {
        if (!_isClass)
            return diff;

        return ReferenceEquals(diff, null) ? source : diff;
    }
}
