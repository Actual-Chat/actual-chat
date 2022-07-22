namespace ActualChat.Diff.Handlers;

public class MissingDiffHandler<T, TDiff> : DiffHandlerBase<T, TDiff>
{
    public MissingDiffHandler(DiffEngine engine) : base(engine) { }

    public override TDiff Diff(T source, T target)
        => throw new NotSupportedException(
            $"No IDiffHandler for source of type '{typeof(T).Name}' and diff of type '{typeof(TDiff).Name}'.");

    public override T Patch(T source, TDiff diff)
        => throw new NotSupportedException(
            $"No IDiffHandler for source of type '{typeof(T).Name}' and diff of type '{typeof(TDiff).Name}'.");
}
