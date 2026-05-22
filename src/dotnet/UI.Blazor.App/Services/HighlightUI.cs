using System.Collections.ObjectModel;
using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public class HighlightUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>> _hits = ReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>>.Empty;

    public void SetHighlightedWords(IReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>> highlightedWords)
    {
        var oldHits = _hits;
        _hits = highlightedWords;
        using (Invalidation.Begin())
            foreach (var entryId in oldHits.Keys.Union(highlightedWords.Keys))
                _ = GetHighlightedWords(entryId, default);
    }

    [ComputeMethod]
    public virtual async Task<SearchMatch> GetMatch(ChatEntryId entryId, string text, CancellationToken cancellationToken)
    {
        var words = await GetHighlightedWords(entryId, cancellationToken).ConfigureAwait(false);
        if (words.Count == 0)
            return SearchMatch.Empty;

        var query = new MemSearchQuery(words.ToDelimitedString(" "));
        return new SearchMatch(text, 0, query.GetMatchParts(text, exact: true));
    }

    [ComputeMethod] // Synced
    public virtual Task<IReadOnlySet<string>> GetHighlightedWords(ChatEntryId chatEntryId, CancellationToken cancellationToken)
        => Task.FromResult(_hits.GetValueOrDefault(chatEntryId, ApiSet<string>.Empty));
}
