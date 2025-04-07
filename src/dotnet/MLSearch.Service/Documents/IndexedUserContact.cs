using OpenSearch.Client;

namespace ActualChat.MLSearch.Documents;

public class IndexedUserContact : IHasId<ContactId>, IHasRoutingKey<ContactId>, IRequirementTarget
{
    public ContactId Id { get; init; }
    public UserId OwnerId => Id.OwnerId;
    public UserId OtherUserId => Id.GetOtherUserId();
    public string Name { get; init; } = "";
    public string ExternalContactName { get; init; } = "";
    public JoinField ContactToUser => JoinField.Link<IndexedUserContact, IndexedUser>(new (OtherUserId));

    public static string GetRoutingKey(ContactId id)
        => id.GetOtherUserId();
}
