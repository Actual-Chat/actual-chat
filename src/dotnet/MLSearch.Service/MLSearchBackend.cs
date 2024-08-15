using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch;

public class MLSearchBackend(IServiceProvider services) : IMLSearchBackend
{
    internal IChatContentDocumentLoader DocumentLoader { get; } =
        services.GetRequiredService<IChatContentDocumentLoader>();

    public virtual async Task<string> GetIndexDocIdByEntryId(ChatEntryId chatEntryId, CancellationToken cancellationToken)
    {
        if (chatEntryId.IsNone || chatEntryId.Kind != ChatEntryKind.Text)
            return "";

        var slices = await DocumentLoader.LoadByEntryIdsAsync(new[] { chatEntryId }, cancellationToken).ConfigureAwait(false);
        if (slices.Count == 0) {
            Computed.GetCurrent().Invalidate(TimeSpan.FromSeconds(20)); // Chat entry will be indexed later.
            return "";
        }

        return slices.First().Id;
    }
}
