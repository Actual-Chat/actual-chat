using ActualChat.Serialization.Internal;
using ActualLab.Versioning;

namespace ActualChat;

/// <summary>
/// Represents a change operation (create, update, or remove) on an entity.
/// </summary>
public interface IChange : IRequirementTarget, ISanitized
{
    ChangeKind Kind { get; }
    bool IsValid();
}

/// <summary>
/// A serializable change record with separate create and update payload types.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackFormatter(typeof(ChangeMessagePackFormatter<,>))]
public partial record Change<TCreate, TUpdate> : IChange
{
    [DataMember, MemoryPackOrder(0)]
    public Option<TCreate> Create { get; init; }
    [DataMember, MemoryPackOrder(1)]
    public Option<TUpdate> Update { get; init; }
    [DataMember, MemoryPackOrder(2)]
    public bool Remove { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChangeKind Kind {
        get {
            this.RequireValid();
            if (Create.HasValue)
                return ChangeKind.Create;
            if (Update.HasValue)
                return ChangeKind.Update;
            return ChangeKind.Remove;
        }
    }

    public bool IsValid()
    {
        var hasCreate = Create.HasValue;
        var hasUpdate = Update.HasValue;
        var hasRemove = Remove;
        return hasCreate != hasUpdate != hasRemove && !(hasCreate && hasUpdate && hasRemove);
    }

    public bool IsCreate(out TCreate create)
    {
        if (!Create.IsSome(out create!))
            return false;
        return true;
    }

    public bool IsUpdate(out TUpdate update)
    {
        if (!Update.IsSome(out update!))
            return false;
        return true;
    }

    public bool IsRemove()
    {
        if (!Remove)
            return false;
        return true;
    }
}

/// <summary>
/// A serializable change record using the same type for create and update payloads.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackFormatter(typeof(ChangeMessagePackFormatter<>))]
public partial record Change<T> : Change<T, T>;

/// <summary>
/// Factory methods for creating <see cref="Change{T}"/> instances.
/// </summary>
public static class Change
{
    public static Change<T> Create<T>(T item)
        => new() {
            Create = item,
        };

    public static Change<T> Update<T>(T item)
        => new() {
            Update = item,
        };

    public static Change<T> Remove<T>(T item = default!)
        => new() {
            Remove = true,
        };

    public static Change<T> Upsert<T>(T item) where T : IHasVersion<long>
        => item.HasVersion() ? Update(item) : Create(item);

    public static Change<T> Upsert<T>(T item, IStringIdentifier? id)
        => id is not null ? Update(item) : Create(item);
}
