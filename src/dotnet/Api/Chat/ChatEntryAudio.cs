using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Audio metadata for a <see cref="ChatEntry"/>. Built from DB columns on reads,
/// enriched with <see cref="Media.Media"/> data for tiles. Stored back to DB via UpdateFrom().
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record ChatEntryAudio
{
    [DataMember, Key(0)] public MediaId? MediaId { get; init; }
    [DataMember, Key(1)] public string BlobId { get; init; } = "";
    [DataMember, Key(2)] public string StreamId { get; init; } = "";
    [DataMember, Key(3)] public Moment BeginsAt { get; init; }
    [DataMember, Key(5)] public Moment? EndsAt { get; init; }
    [DataMember, Key(6)] public Moment? ContentEndsAt { get; init; }
    [DataMember, Key(7)] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember, Key(4)] public LinearMap TimeMap { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public double? Duration => EndsAt is { } endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsStreaming => !StreamId.IsNullOrEmpty();

    [SerializationConstructor]
    public ChatEntryAudio() { }

    // This record relies on referential equality
    public bool Equals(ChatEntryAudio? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
