using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Read-only audio metadata resolved from <see cref="Media.Media"/> when a <see cref="ChatEntry"/>
/// has a valid <see cref="MediaId"/> in its <see cref="ChatEntry.MediaOrStreamId"/> field.
/// Populated on reads, not stored directly.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record ChatEntryAudio
{
    [DataMember, MemoryPackOrder(0)] public MediaId MediaId { get; init; } = null!;
    [DataMember, MemoryPackOrder(1)] public string ContentId { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public Moment BeginsAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public Moment? EndsAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public Moment? ContentEndsAt { get; init; }
    [DataMember, MemoryPackOrder(5)] public Moment? ClientSideBeginsAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public double? Duration => EndsAt is { } endsAt ? (endsAt - BeginsAt).TotalSeconds : null;

    [MemoryPackConstructor, SerializationConstructor]
    public ChatEntryAudio() { }

    // This record relies on referential equality
    public bool Equals(ChatEntryAudio? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
