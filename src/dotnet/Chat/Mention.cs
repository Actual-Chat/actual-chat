using MemoryPack;
using Stl.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Mention : IHasId<Symbol>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public ChatEntryId EntryId { get; init; }
    [DataMember, MemoryPackOrder(2)] public MentionId MentionId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId => EntryId.ChatId;
}
