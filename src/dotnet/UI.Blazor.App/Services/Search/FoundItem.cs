using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record FoundItem(
    SearchResult SearchResult,
    SearchScope Scope,
    bool IsFirstInGroup = false,
    bool IsLastInGroup = false,
    bool CanScopeBeExpanded = false)
{
    public ChatId ChatId => SearchResult switch {
        ContactSearchResult contact => contact.ContactId.ChatId,
        EntrySearchResult entry => entry.EntryId.ChatId,
        _ => throw new ArgumentOutOfRangeException()
    };
    public TextEntryId EntryId
        => SearchResult is EntrySearchResult entry ? entry.EntryId : TextEntryId.None;
    public SearchMatch ContactSearchMatch
        => SearchResult is ContactSearchResult ? SearchResult.SearchMatch : SearchMatch.Empty;
    public SearchMatch MessageSearchMatch
        => SearchResult is EntrySearchResult ? SearchResult.SearchMatch : SearchMatch.Empty;

    public override string ToString()
        => $"{(IsFirstInGroup ? "|" : " ")} {Scope}: {SearchResult.Text} {(IsLastInGroup ? "|" : "")}";
}
