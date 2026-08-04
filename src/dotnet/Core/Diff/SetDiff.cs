using ActualChat.Serialization.Internal;

namespace ActualChat.Diff;

/// <summary>
/// Represents changes to a collection as added and removed items.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[DataContract, MessagePackFormatter(typeof(SetDiffMessagePackFormatter<>))]
[method: SerializationConstructor]
public readonly partial struct SetDiff<TItem>(
    TItem[]? addedItems,
    TItem[]? removedItems = null
) : IDiff, IEquatable<SetDiff<TItem>>
{
    public static readonly SetDiff<TItem> Unchanged = default!;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsEmpty => AddedItems.Length == 0 && RemovedItems.Length == 0;

    [DataMember(Order = 0)]
    public TItem[] AddedItems {
        get => field ?? [];
        init;
    } = addedItems!;

    [DataMember(Order = 1)]
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

/// <summary>
/// Represents changes to a typed collection as added and removed items.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[DataContract, MessagePackFormatter(typeof(SetDiffMessagePackFormatter<,>))]
[method: SerializationConstructor]
public readonly partial struct SetDiff<TCollection, TItem>(
    TItem[]? addedItems,
    TItem[]? removedItems = null
    ) : IDiff, IEquatable<SetDiff<TCollection, TItem>>
    where TCollection : IReadOnlyCollection<TItem>
{
    public static readonly SetDiff<TCollection, TItem> Unchanged = default!;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsEmpty => AddedItems.Length == 0 && RemovedItems.Length == 0;

    [DataMember(Order = 0)]
    public TItem[] AddedItems {
        get => field ?? [];
        init;
    } = addedItems!;

    [DataMember(Order = 1)]
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
