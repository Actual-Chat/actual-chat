namespace ActualChat.Diff;

[DataContract]
public record SetDiff<TCollection, TItem> : IDiff
    where TCollection : IReadOnlyCollection<TItem>
{
    public static SetDiff<TCollection, TItem> Unchanged { get; } = new();

    [DataMember] public ImmutableArray<TItem> AddedItems { get; init; } = ImmutableArray<TItem>.Empty;
    [DataMember] public ImmutableArray<TItem> RemovedItems { get; init; } = ImmutableArray<TItem>.Empty;

    public SetDiff() { }
    public SetDiff(ImmutableArray<TItem> addedItems, ImmutableArray<TItem> removedItems)
    {
        AddedItems = addedItems;
        RemovedItems = removedItems;
    }

    public bool IsEmpty() => AddedItems.IsEmpty && RemovedItems.IsEmpty;
}
