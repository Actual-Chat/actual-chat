using OpenSearch.Client;

namespace ActualChat.MLSearch.Documents;

public sealed record IndexedEntry : IHasId<TextEntryId>, IHasRoutingKey<TextEntryId>
{
    public TextEntryId Id { get; init; }
    public string Content { get; init; } = "";
    public JoinField EntryToChat => JoinField.Link<IndexedEntry, IndexedChat>(new (ChatId));

    // Computed
    public ChatId ChatId => Id.ChatId;

    public static string GetRoutingKey(TextEntryId id)
        => id.ChatId;
}
