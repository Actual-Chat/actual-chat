using ActualLab.Fusion.Blazor;

namespace ActualChat.Media;

/// <summary>
/// Represents a file upload session with metadata.
/// </summary>
#pragma warning disable MA0049 // Allows ActualChat.Media.Media

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record Upload : IHasId<UploadId>, IHasMetadata, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0), Key(0)] public UploadId Id { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public UserId UserId { get; init; }
    #region MemoryPackXxx properties

    [MemoryPackInclude, MemoryPackOrder(2)]
    private ApiNullable8<long> MemoryPackLength {
        get => Length;
        init => Length = value;
    }

    #endregion

    [DataMember, MemoryPackIgnore, Key(5)] public long? Length { get; init; }
    [DataMember, MemoryPackOrder(3), Key(2)] public string Tag { get; init; } = "";
    [DataMember, MemoryPackOrder(4), Key(3)] public string SessionUri { get; init; } = "";
    [DataMember, MemoryPackOrder(10), Key(4)] public PropertyBag Metadata { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string FileName {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
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

    [MemoryPackConstructor]
    private Upload() : this(default!, default!, null, "", default) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public Upload(UploadId id, UserId userId, string tag, string sessionUri, PropertyBag metadata, long? length)
        : this(id, userId, length, tag, metadata)
        => SessionUri = sessionUri;

    // This record relies on referential equality
    public bool Equals(Upload? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
