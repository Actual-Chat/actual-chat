using OpenSearch.Client;

namespace ActualChat.MLSearch.Documents;

public sealed record IndexedUser(UserId Id) : IHasId<UserId>, IHasRoutingKey<UserId>, IRequirementTarget
{
    public string Name { get; init; } = "";
    public ApiArray<PlaceId> PlaceIds { get; init; }
    public JoinField ContactToUser { get; set; } = JoinField.Root<IndexedUser>();
}
