using MemoryPack;

namespace ActualChat;

public interface IChange : IRequirementTarget
{
    ChangeKind Kind { get; }
    bool IsValid();
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Change<TCreate, TUpdate> : IChange
{
    [DataMember, MemoryPackOrder(0)]
    public Option<TCreate> Create { get; init; }
    [DataMember, MemoryPackOrder(1)]
    public Option<TUpdate> Update { get; init; }
    [DataMember, MemoryPackOrder(2)]
    public bool Remove { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
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

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Change<T> : Change<T, T>;
