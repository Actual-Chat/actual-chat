using MemoryPack;

namespace ActualChat.Diff;

[StructLayout(LayoutKind.Sequential)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor]
public readonly partial struct SetDiff<TCollection, TItem>(
    ApiArray<TItem> addedItems,
    ApiArray<TItem> removedItems = default
    ) : IDiff, IEquatable<SetDiff<TCollection, TItem>> where TCollection : IReadOnlyCollection<TItem>
{
    public static readonly SetDiff<TCollection, TItem> Unchanged = default!;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => AddedItems.IsEmpty && RemovedItems.IsEmpty;

    [DataMember(Order = 0), MemoryPackOrder(0)] public ApiArray<TItem> AddedItems { get; init; } = addedItems;
    [DataMember(Order = 1), MemoryPackOrder(1)] public ApiArray<TItem> RemovedItems { get; init; } = removedItems;

    // Equality
    public bool Equals(SetDiff<TCollection, TItem> other)
        => AddedItems.Equals(other.AddedItems) && RemovedItems.Equals(other.RemovedItems);
    public override bool Equals(object? obj)
        => obj is SetDiff<TCollection, TItem> other && Equals(other);
    public override int GetHashCode()
        => HashCode.Combine(AddedItems, RemovedItems);
    public static bool operator ==(SetDiff<TCollection, TItem> left, SetDiff<TCollection, TItem> right)
        => left.Equals(right);
    public static bool operator !=(SetDiff<TCollection, TItem> left, SetDiff<TCollection, TItem> right)
        => !left.Equals(right);
}
