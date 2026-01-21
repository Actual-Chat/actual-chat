using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Upload : IHasId<UploadId>, IHasMetadata, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public UploadId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public UserId UserId { get; init; }
    [DataMember, MemoryPackOrder(2)] public long? Length { get; init; }
    [DataMember, MemoryPackOrder(3)] public string Tag { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public string SessionUri { get; init; } = "";
    [DataMember, MemoryPackOrder(10)] public PropertyBag Metadata { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string FileName {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string ContentType {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    public Upload(UploadId id, UserId userId, long? length, string tag, PropertyBag metadata)
    {
        Id = id;
        UserId = userId;
        Length = length;
        Tag = tag;
        Metadata = metadata;
    }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Upload(UploadId id, UserId userId, long? length, string tag, string sessionUri, PropertyBag metadata)
        : this(id, userId, length, tag, metadata)
        => SessionUri = sessionUri;

    // This record relies on referential equality
    public bool Equals(Upload? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
