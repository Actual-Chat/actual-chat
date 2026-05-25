using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Audio metadata for a <see cref="ChatEntry"/>. Built from DB columns on reads,
/// enriched with <see cref="Media.Media"/> data for tiles. Stored back to DB via UpdateFrom().
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatEntryAudio
{
    #region MemoryPackXxx properties

    [MemoryPackInclude, MemoryPackOrder(10)]
    private ApiNullable8<Moment> MemoryPackEndsAt {
        get => EndsAt;
        init => EndsAt = value;
    }

    [MemoryPackInclude, MemoryPackOrder(11)]
    private ApiNullable8<Moment> MemoryPackContentEndsAt {
        get => ContentEndsAt;
        init => ContentEndsAt = value;
    }

    [MemoryPackInclude, MemoryPackOrder(12)]
    private ApiNullable8<Moment> MemoryPackClientSideBeginsAt {
        get => ClientSideBeginsAt;
        init => ClientSideBeginsAt = value;
    }

    #endregion

    [DataMember, MemoryPackOrder(0), Key(0)] public MediaId? MediaId { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public string BlobId { get; init; } = "";
    [DataMember, MemoryPackOrder(2), Key(2)] public string StreamId { get; init; } = "";
    [DataMember, MemoryPackOrder(3), Key(3)] public Moment BeginsAt { get; init; }
    [DataMember, MemoryPackIgnore, Key(5)] public Moment? EndsAt { get; init; }
    [DataMember, MemoryPackIgnore, Key(6)] public Moment? ContentEndsAt { get; init; }
    [DataMember, MemoryPackIgnore, Key(7)] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public LinearMap TimeMap { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public double? Duration => EndsAt is { } endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsStreaming => !StreamId.IsNullOrEmpty();

    [MemoryPackConstructor, SerializationConstructor]
    public ChatEntryAudio() { }

    // This record relies on referential equality
    public bool Equals(ChatEntryAudio? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
