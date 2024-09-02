using OpenSearch.Client;

namespace ActualChat.MLSearch.Documents;

public sealed record IndexedChat(ChatId Id) : IHasId<ChatId>, IHasRoutingKey<ChatId>
{
    public PlaceId PlaceId { get; init; }
    public bool IsPublic { get; init; }
    public bool IsPublicInPlace { get; init; }
    public JoinField EntryToChat { get; set; } = JoinField.Root<IndexedChat>();
}
