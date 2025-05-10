using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Mention : IHasId<Symbol>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public required ChatEntryId EntryId { get; init; }
    [DataMember, MemoryPackOrder(2)] public required MentionId MentionId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId => EntryId.ChatId;

    // This record relies on referential equality
    public bool Equals(Mention? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
