using MemoryPack;

namespace ActualChat.Diff;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record SetDiff<TCollection, TItem> : IDiff
    where TCollection : IReadOnlyCollection<TItem>
{
    public static SetDiff<TCollection, TItem> Unchanged { get; } = new();

    [DataMember, MemoryPackOrder(0)] public ImmutableArray<TItem> AddedItems { get; init; } = ImmutableArray<TItem>.Empty;
    [DataMember, MemoryPackOrder(1)] public ImmutableArray<TItem> RemovedItems { get; init; } = ImmutableArray<TItem>.Empty;

    [MemoryPackConstructor]
    public SetDiff() { }
    public SetDiff(ImmutableArray<TItem> addedItems, ImmutableArray<TItem> removedItems)
    {
        AddedItems = addedItems;
        RemovedItems = removedItems;
    }

    public bool IsEmpty() => AddedItems.IsEmpty && RemovedItems.IsEmpty;
}
