using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a mention of a user or author in a chat entry.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Mention : IHasId<Symbol>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0), Key(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public required ChatEntryId EntryId { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public required MentionRef MentionRef { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ChatId => EntryId.ChatId;

    // This record relies on referential equality
    public bool Equals(Mention? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
