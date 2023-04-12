using Stl.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract]
public sealed record Mention : IHasId<Symbol>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; }
    [DataMember] public ChatEntryId EntryId { get; init; }
    [DataMember] public MentionId MentionId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => EntryId.ChatId;
}
