using System.Collections.ObjectModel;
using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public class HighlightUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>> _wordsByChatEntryId
        = ReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>>.Empty;

    public void Set(IReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>> wordsByChatEntryId)
    {
        var oldWordsByChatEntryId = _wordsByChatEntryId;
        _wordsByChatEntryId = wordsByChatEntryId;
        using (Invalidation.Begin())
            foreach (var entryId in oldWordsByChatEntryId.Keys.Union(wordsByChatEntryId.Keys)) {
                _ = GetSearchQuery(entryId, default);
                _ = GetWordSet(entryId, default);
            }
    }

    public void Reset()
        => Set(ReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>>.Empty);

    public virtual async Task<SearchMatch> GetSearchMatch(ChatEntryId entryId, string text, CancellationToken cancellationToken)
    {
        var query = await GetSearchQuery(entryId, cancellationToken).ConfigureAwait(false);
        return query.IsEmpty
            ? SearchMatch.Empty
            : new SearchMatch(text, query);
    }

    [ComputeMethod]
    public virtual Task<SearchQuery> GetSearchQuery(ChatEntryId chatEntryId, CancellationToken cancellationToken)
    {
        var wordSet = _wordsByChatEntryId.GetValueOrDefault(chatEntryId, new ApiSet<string>());
        var searchQuery = new SearchQuery(wordSet.ToDelimitedString(" "), matchSuffixes: true);
        return Task.FromResult(searchQuery);
    }

    [ComputeMethod]
    public virtual Task<IReadOnlySet<string>> GetWordSet(ChatEntryId chatEntryId, CancellationToken cancellationToken)
    {
        var wordSet = _wordsByChatEntryId.GetValueOrDefault(chatEntryId, new ApiSet<string>());
        return Task.FromResult(wordSet);
    }
}
