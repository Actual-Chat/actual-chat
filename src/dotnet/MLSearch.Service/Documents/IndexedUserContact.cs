using OpenSearch.Client;

namespace ActualChat.MLSearch.Documents;

public class IndexedUserContact : IHasId<ContactId>, IHasRoutingKey<ContactId>, IRequirementTarget
{
    public required ContactId Id { get; init; }
    public UserId OwnerId => Id.OwnerId;
    public UserId? OtherUserId => Id.GetOtherUserId();
    public string Name { get; init; } = "";
    public string ExternalContactName { get; init; } = "";

    // TODO(AY): Verify this
    public JoinField? ContactToUser => OtherUserId is { } otherUserId
        ? JoinField.Link<IndexedUserContact, IndexedUser>(new IndexedUser(otherUserId))
        : null;

    public static string? GetRoutingKey(ContactId? id)
        => id?.GetOtherUserId()?.Value;
}
