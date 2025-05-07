using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch;

internal class MLSearchBackend(IChatContentDocumentLoader documentLoader) : IMLSearchBackend
{
    private IChatContentDocumentLoader DocumentLoader { get; } = documentLoader;

    public virtual async Task<string> GetIndexDocIdByEntryId(ChatEntryId? entryId, CancellationToken cancellationToken)
    {
        if (entryId is not TextEntryId textEntryId)
            return "";

        var slices = await DocumentLoader.LoadByEntryIdsAsync([textEntryId], cancellationToken).ConfigureAwait(false);
        if (slices.Count == 0) {
            Computed.GetCurrent().Invalidate(TimeSpan.FromSeconds(20)); // Chat entry will be indexed later.
            return "";
        }

        return slices.First().Id;
    }
}
