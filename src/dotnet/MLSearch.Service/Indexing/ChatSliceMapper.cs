using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing;

internal sealed class ChatSliceMapper : IDocumentMapper<ChatEntry, ChatEntry, ChatSlice>
{
    public ChatSlice Map(ChatEntry source)
        => source.IntoIndexedDocument();

    public string MapId(ChatEntry source)
        => source.IntoDocumentId();
}
