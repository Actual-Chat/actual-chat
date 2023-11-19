namespace ActualChat.Diff.Handlers;

public class SetDiffHandler<TSet, TItem> : DiffHandlerBase<TSet, SetDiff<TSet, TItem>>
    where TSet : IReadOnlyCollection<TItem>
{
    private readonly Type _setType;
    private readonly Type? _setGenericType;

    public SetDiffHandler(DiffEngine engine) : base(engine)
    {
        _setType = typeof(TSet);
        _setGenericType = _setType.IsConstructedGenericType
            ? _setType.GetGenericTypeDefinition()
            : null;
    }

    public override SetDiff<TSet, TItem> Diff(TSet source, TSet target)
    {
        var added = target.Except(source).ToApiArray();
        var removed = source.Except(target).ToApiArray();
        return new SetDiff<TSet, TItem>(added, removed);
    }

#pragma warning disable IL2077
    public override TSet Patch(TSet source, SetDiff<TSet, TItem> diff)
    {
        var removedItems = diff.RemovedItems.ToHashSet();
        var target = source
            .Where(i => !removedItems.Contains(i))
            .Concat(diff.AddedItems);
        if (_setGenericType == null)
            return (TSet)_setType.CreateInstance(target);
        if (_setGenericType == typeof(ApiArray<>))
            return (TSet)(object)new ApiArray<TItem>(target);
        if (_setGenericType == typeof(ImmutableArray<>))
            return (TSet)(object)ImmutableArray.Create(target);
        if (_setGenericType == typeof(ImmutableList<>))
            return (TSet)(object)ImmutableList.Create(target);
        if (_setGenericType == typeof(ImmutableHashSet<>))
            return (TSet)(object)ImmutableHashSet.Create(target);
        return (TSet)_setType.CreateInstance(target);
    }
#pragma warning restore IL2077
}
