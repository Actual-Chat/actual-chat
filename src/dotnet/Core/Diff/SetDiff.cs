using MemoryPack;

namespace ActualChat.Diff;

[StructLayout(LayoutKind.Sequential)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor]
public readonly partial struct SetDiff<TCollection, TItem>(
    ApiArray<TItem> addedItems,
    ApiArray<TItem> removedItems = default
    ) : IDiff
    where TCollection : IReadOnlyCollection<TItem>
{
    public static readonly SetDiff<TCollection, TItem> Unchanged = default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => AddedItems.IsEmpty && RemovedItems.IsEmpty;

    [DataMember(Order = 0), MemoryPackOrder(0)] public ApiArray<TItem> AddedItems { get; init; } = addedItems;
    [DataMember(Order = 1), MemoryPackOrder(1)] public ApiArray<TItem> RemovedItems { get; init; } = removedItems;
}
