namespace ActualChat.Chat;

public record Mention : IHasId<Symbol>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; } = "";
    [DataMember] public ChatEntryId EntryId { get; init; }
    [DataMember] public AuthorId AuthorId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => EntryId.ChatId;
}
