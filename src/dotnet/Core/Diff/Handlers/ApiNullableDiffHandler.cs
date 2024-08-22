namespace ActualChat.Diff.Handlers;

public class ApiNullableDiffHandler<T>(DiffEngine engine) : DiffHandlerBase<T, ApiNullable<T>>(engine)
    where T : struct
{
    public override ApiNullable<T> Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? null : target;

    public override T Patch(T source, ApiNullable<T> diff)
        => diff.Nullable ?? source;
}

public class ApiNullable2DiffHandler<T>(DiffEngine engine) : DiffHandlerBase<T, ApiNullable2<T>>(engine)
    where T : struct
{
    public override ApiNullable2<T> Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? null : target;

    public override T Patch(T source, ApiNullable2<T> diff)
        => diff.Nullable ?? source;
}

public class ApiNullable4DiffHandler<T>(DiffEngine engine) : DiffHandlerBase<T, ApiNullable4<T>>(engine)
    where T : struct
{
    public override ApiNullable4<T> Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? null : target;

    public override T Patch(T source, ApiNullable4<T> diff)
        => diff.Nullable ?? source;
}

public class ApiNullable8DiffHandler<T>(DiffEngine engine) : DiffHandlerBase<T, ApiNullable8<T>>(engine)
    where T : struct
{
    public override ApiNullable8<T> Diff(T source, T target)
        => EqualityComparer<T>.Default.Equals(source, target) ? null : target;

    public override T Patch(T source, ApiNullable8<T> diff)
        => diff.Nullable ?? source;
}
