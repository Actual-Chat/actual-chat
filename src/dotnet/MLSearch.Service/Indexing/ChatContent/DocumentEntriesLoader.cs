using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

public interface IDocumentEntriesLoader
{
    ValueTask<IReadOnlyList<ChatEntry>> LoadTailEntries(
        ChatSlice tailDocument,
        CancellationToken cancellationToken);
}

internal sealed class DefaultDocumentEntriesLoader(IChatsBackend chatsBackend) : IDocumentEntriesLoader
{
    public async ValueTask<IReadOnlyList<ChatEntry>> LoadTailEntries(
        ChatSlice tailDocument, CancellationToken cancellationToken)
    {
        var tailEntryIds = tailDocument.Metadata
            .ChatEntries
            .Select(e => e.Id);
        return await chatsBackend.GetEntries(tailEntryIds, false, cancellationToken).ConfigureAwait(false);
    }
}
