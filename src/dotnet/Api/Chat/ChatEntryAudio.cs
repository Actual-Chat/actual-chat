using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Audio metadata for a <see cref="ChatEntry"/>. Built from DB columns on reads,
/// enriched with <see cref="Media.Media"/> data for tiles. Stored back to DB via UpdateFrom().
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record ChatEntryAudio
{
    #region MemoryPackXxx properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(10), IgnoreMember]
    private ApiNullable8<Moment> MemoryPackEndsAt {
        get => EndsAt;
        init => EndsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(11), IgnoreMember]
    private ApiNullable8<Moment> MemoryPackContentEndsAt {
        get => ContentEndsAt;
        init => ContentEndsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(12), IgnoreMember]
    private ApiNullable8<Moment> MemoryPackClientSideBeginsAt {
        get => ClientSideBeginsAt;
        init => ClientSideBeginsAt = value;
    }

    #endregion

    [DataMember, MemoryPackOrder(0)] public MediaId? MediaId { get; init; }
    [DataMember, MemoryPackOrder(1)] public string BlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public string StreamId { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public Moment BeginsAt { get; init; }
    [DataMember, MemoryPackIgnore] public Moment? EndsAt { get; init; }
    [DataMember, MemoryPackIgnore] public Moment? ContentEndsAt { get; init; }
    [DataMember, MemoryPackIgnore] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public LinearMap TimeMap { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public double? Duration => EndsAt is { } endsAt ? (endsAt - BeginsAt).TotalSeconds : null;

    [MemoryPackConstructor, SerializationConstructor]
    public ChatEntryAudio() { }

    // This record relies on referential equality
    public bool Equals(ChatEntryAudio? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
