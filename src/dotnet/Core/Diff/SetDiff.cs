using MemoryPack;

namespace ActualChat.Diff;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record SetDiff<TCollection, TItem> : IDiff
    where TCollection : IReadOnlyCollection<TItem>
{
    public static SetDiff<TCollection, TItem> Unchanged { get; } = new();

    [DataMember, MemoryPackOrder(0)] public ApiArray<TItem> AddedItems { get; init; } = ApiArray<TItem>.Empty;
    [DataMember, MemoryPackOrder(1)] public ApiArray<TItem> RemovedItems { get; init; } = ApiArray<TItem>.Empty;

    [MemoryPackConstructor]
    public SetDiff() { }
    public SetDiff(ApiArray<TItem> addedItems, ApiArray<TItem> removedItems)
    {
        AddedItems = addedItems;
        RemovedItems = removedItems;
    }

    public bool IsEmpty() => AddedItems.IsEmpty && RemovedItems.IsEmpty;
}
