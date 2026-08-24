using OpenSearch.Client;

namespace ActualChat.MLSearch.Documents;

public sealed record IndexedEntry : IHasId<ChatEntryId>, IHasRoutingKey<ChatEntryId>
{
    public required ChatEntryId Id { get; init; }
    public string Content { get; init; } = "";
    public string[] Hashtags { get; init; } = [];
    public Moment At { get; init; }
    public JoinField EntryToChat => JoinField.Link<IndexedEntry, IndexedChat>(new (ChatId));

    // Computed
    public ChatId ChatId => Id.ChatId;

    public static string GetRoutingKey(ChatEntryId? id)
        => id?.ChatId.Value ?? "";
}
