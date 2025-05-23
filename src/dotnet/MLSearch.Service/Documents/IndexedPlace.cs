namespace ActualChat.MLSearch.Documents;

public sealed record IndexedPlace : IHasId<PlaceId>, IHasRoutingKey<PlaceId>, IRequirementTarget
{
    public required PlaceId Id { get; init; }
    public string Title { get; init; } = "";
    public bool IsPublic { get; init; }
}
