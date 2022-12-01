namespace ActualChat.Chat;

[DataContract]
public record Mention : IHasId<Symbol>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; }
    [DataMember] public ChatEntryId EntryId { get; init; }
    [DataMember] public Symbol MentionId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => EntryId.ChatId;
}
