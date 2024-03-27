using ActualChat.Chat;
using OpenSearch.Client;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal class ChatSliceMapper : IDocumentMapper<ChatEntry, ChatSlice>
{
    public ChatSlice Map(ChatEntry source)
        => source.IntoIndexedDocument();

    public Id MapId(ChatEntry source)
        => source.IntoDocumentId();
}
