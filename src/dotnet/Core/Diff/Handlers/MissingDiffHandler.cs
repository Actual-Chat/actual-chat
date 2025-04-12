namespace ActualChat.Diff.Handlers;

public sealed class MissingDiffHandler<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDiff>(DiffEngine engine)
    : DiffHandlerBase<T, TDiff>(engine)
{
    public override TDiff Diff(T source, T target)
        => throw StandardError.NotSupported(
            $"No IDiffHandler for source of type '{typeof(T).GetName()}' and diff of type '{typeof(TDiff).GetName()}'.");

    public override T Patch(T source, TDiff diff)
        => throw StandardError.NotSupported(
            $"No IDiffHandler for source of type '{typeof(T).GetName()}' and diff of type '{typeof(TDiff).GetName()}'.");
}
