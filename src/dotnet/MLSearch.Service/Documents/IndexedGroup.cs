namespace ActualChat.MLSearch.Documents;

public sealed record IndexedGroup : IHasId<ChatId>, IHasRoutingKey<ChatId>, IRequirementTarget
{
    public required ChatId Id { get; init; }
    public PlaceId? PlaceId { get; init; }
    public string Title { get; init; } = "";
    public bool IsPublic { get; init; }
}
