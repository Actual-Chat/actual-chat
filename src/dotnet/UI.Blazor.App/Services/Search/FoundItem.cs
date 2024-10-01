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

    public LocalUrl Link => Scope switch {
        SearchScope.Groups => Links.Chat(ChatId),
        SearchScope.People => Links.Chat(ChatId),
        SearchScope.Places => Links.PlaceInfo(ChatId.PlaceId),
        SearchScope.Messages => Links.Chat(ChatId, EntryId.LocalId),
        _ => throw new ArgumentOutOfRangeException(nameof(Scope), Scope, null),
    };

    public override string ToString()
        => $"{(IsFirstInGroup ? "|" : " ")} {Scope}: {SearchResult.Text} {(IsLastInGroup ? "|" : "")}";
}
