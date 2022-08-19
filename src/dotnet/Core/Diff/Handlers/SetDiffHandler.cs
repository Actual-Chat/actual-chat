using Stl.Extensibility;
using Stl.Reflection;

namespace ActualChat.Diff.Handlers;

[MatchFor(typeof(SetDiff<,>), typeof(IDiffHandler))]
public class SetDiffHandler<TCollection, TItem> : DiffHandlerBase<TCollection, SetDiff<TCollection, TItem>>
    where TCollection : IReadOnlyCollection<TItem>
{
    private readonly Type _collectionType;
    private readonly Type? _collectionGenericType;

    public SetDiffHandler(DiffEngine engine) : base(engine)
    {
        _collectionType = typeof(TCollection);
        _collectionGenericType = _collectionType.IsConstructedGenericType
            ? _collectionType.GetGenericTypeDefinition()
            : null;
    }

    public override SetDiff<TCollection, TItem> Diff(TCollection source, TCollection target)
    {
        var added = target.Except(source).ToImmutableArray();
        var removed = source.Except(target).ToImmutableArray();
        return new SetDiff<TCollection, TItem>(added, removed);
    }

    public override TCollection Patch(TCollection source, SetDiff<TCollection, TItem> diff)
    {
        var removedItems = diff.RemovedItems.ToHashSet();
        var target = source.Where(i => !removedItems.Contains(i)).Concat(diff.AddedItems);
        if (_collectionGenericType == null)
            return (TCollection) _collectionType.CreateInstance(target);
        if (_collectionGenericType == typeof(ImmutableArray<>))
            return (TCollection) (object) ImmutableArray.Create(target.ToArray());
        if (_collectionGenericType == typeof(ImmutableList<>))
            return (TCollection) (object) ImmutableList.Create(target.ToArray());
        if (_collectionGenericType == typeof(ImmutableHashSet<>))
            return (TCollection) (object) ImmutableHashSet.Create(target.ToArray());
        return (TCollection) _collectionType.CreateInstance(target);
    }
}
