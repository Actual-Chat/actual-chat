namespace ActualChat.Diff.Handlers;

public class OptionDiffHandler<T> : DiffHandlerBase<T, Option<T>>
{
    public OptionDiffHandler(DiffEngine engine) : base(engine) { }

    public override Option<T> Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? default : Option.Some(target);

    public override T Patch(T source, Option<T> diff)
        => diff.IsSome(out var value) ? value : source;
}
