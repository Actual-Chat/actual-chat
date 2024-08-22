namespace ActualChat.Diff.Handlers;

public class OptionDiffHandler<T>(DiffEngine engine) : DiffHandlerBase<T, Option<T>>(engine)
{
    public override Option<T> Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? default : Option.Some(target);

    public override T Patch(T source, Option<T> diff)
        => diff.IsSome(out var value) ? value : source;
}
