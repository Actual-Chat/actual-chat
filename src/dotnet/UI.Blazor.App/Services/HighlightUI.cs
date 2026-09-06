using System.Collections.ObjectModel;
using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public class HighlightUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly IReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>> NoWords
        = ReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>>.Empty;

    // A state rather than a field: the search worker writes it while compute methods read it on other
    // threads, and Use() is what makes those recompute exactly when it changes - a plain field they read
    // just before the write, but registered after its invalidation pass, would keep stale words for good
    private readonly MutableState<IReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>>> _wordsByChatEntryId
        = hub.StateFactory.NewMutable(NoWords);

    public void Set(IReadOnlyDictionary<ChatEntryId, IReadOnlySet<string>> wordsByChatEntryId)
        => _wordsByChatEntryId.Value = wordsByChatEntryId;

    public void Reset()
        => Set(NoWords);

    public virtual async Task<SearchMatch> GetSearchMatch(ChatEntryId entryId, string text, CancellationToken cancellationToken)
    {
        var query = await GetSearchQuery(entryId, cancellationToken).ConfigureAwait(false);
        return query.IsEmpty
            ? SearchMatch.Empty
            : new SearchMatch(text, query);
    }

    [ComputeMethod]
    public virtual async Task<SearchQuery> GetSearchQuery(ChatEntryId chatEntryId, CancellationToken cancellationToken)
    {
        var wordSet = await GetWordSet(chatEntryId, cancellationToken).ConfigureAwait(false);
        return new SearchQuery(wordSet.ToDelimitedString(" "), matchSuffixes: true);
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlySet<string>> GetWordSet(ChatEntryId chatEntryId, CancellationToken cancellationToken)
    {
        var wordsByChatEntryId = await _wordsByChatEntryId.Use(cancellationToken).ConfigureAwait(false);
        return wordsByChatEntryId.GetValueOrDefault(chatEntryId, new ApiSet<string>());
    }
}
