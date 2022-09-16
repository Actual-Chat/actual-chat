namespace ActualChat.Chat;

public class Mention : IHasId<Symbol>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; } = "";
    [DataMember] public string AuthorId { get; init; } = "";
    [DataMember] public string ChatId { get; init; } = "";
    [DataMember] public long EntryId { get; init; }
}
