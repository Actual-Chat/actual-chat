using MemoryPack;

namespace ActualChat.Diff;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor]
public readonly partial struct SetDiff<TItem>(
    TItem[]? addedItems,
    TItem[]? removedItems = null
) : IDiff, IEquatable<SetDiff<TItem>>
{
    public static readonly SetDiff<TItem> Unchanged = default!;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => AddedItems.Length == 0 && RemovedItems.Length == 0;

    [DataMember(Order = 0), MemoryPackOrder(0), MemoryPackAllowSerialize]
    [field: MaybeNull, AllowNull]
    public TItem[] AddedItems {
        get => field ?? [];
        init;
    } = addedItems!;

    [DataMember(Order = 1), MemoryPackOrder(1), MemoryPackAllowSerialize]
    [field: MaybeNull, AllowNull]
    public TItem[] RemovedItems {
        get => field ?? [];
        init;
    } = removedItems!;

    // Equality
    public bool Equals(SetDiff<TItem> other)
        => AddedItems.Equals(other.AddedItems) && RemovedItems.Equals(other.RemovedItems);
    public override bool Equals(object? obj)
        => obj is SetDiff<TItem> other && Equals(other);
    public override int GetHashCode()
        => HashCode.Combine(AddedItems, RemovedItems);
    public static bool operator ==(SetDiff<TItem> left, SetDiff<TItem> right)
        => left.Equals(right);
    public static bool operator !=(SetDiff<TItem> left, SetDiff<TItem> right)
        => !left.Equals(right);
}

// SetDiff<T> version which also "encodes" original collection type.
// It's the one used by SetDiffHandler.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor]
public readonly partial struct SetDiff<TCollection, TItem>(
    TItem[]? addedItems,
    TItem[]? removedItems = null
    ) : IDiff, IEquatable<SetDiff<TCollection, TItem>>
    where TCollection : IReadOnlyCollection<TItem>
{
    public static readonly SetDiff<TCollection, TItem> Unchanged = default!;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => AddedItems.Length == 0 && RemovedItems.Length == 0;

    [DataMember(Order = 0), MemoryPackOrder(0), MemoryPackAllowSerialize]
    [field: MaybeNull, AllowNull]
    public TItem[] AddedItems {
        get => field ?? [];
        init;
    } = addedItems!;

    [DataMember(Order = 1), MemoryPackOrder(1), MemoryPackAllowSerialize]
    [field: MaybeNull, AllowNull]
    public TItem[] RemovedItems {
        get => field ?? [];
        init;
    } = removedItems!;

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
