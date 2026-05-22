using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record FoundItem(
    IHasSearchMatch Item,
    SearchScope Scope,
    bool IsGlobalSearchResult,
    bool IsFirstInGroup = false,
    bool IsLastInGroup = false,
    bool CanScopeBeExpanded = false,
    bool IsGlobalSearchPlaceholder = false)
{
    public ChatId ChatId => Item switch {
        FoundContact contact => contact.ContactId.ChatId,
        FoundChatEntry chatEntry => chatEntry.EntryId.ChatId,
        _ => throw new ArgumentOutOfRangeException(),
    };
    public ChatEntryId? EntryId => Item is FoundChatEntry entry ? entry.EntryId : null;
    public SearchMatch? ContactSearchMatch => Item is FoundContact x ? x.Match : null;
    public SearchMatch? ChatEntrySearchMatch => Item is FoundChatEntry x ? x.Match : null;
    public ApiSet<string> HighlightedWords => Item is FoundChatEntry x ? x.HighlightedWords : [];

    public LocalUrl Link => Scope switch {
        SearchScope.Groups => Links.Chat(ChatId),
        SearchScope.People => Links.Chat(ChatId),
        SearchScope.Places => Links.PlaceInfo(((PlaceChatId)ChatId).PlaceId),
        SearchScope.Messages => Links.Chat(ChatId, EntryId?.LocalId ?? 0),
        _ => throw new ArgumentOutOfRangeException(nameof(Scope), Scope, null),
    };

    public override string ToString()
        => $"{(IsFirstInGroup ? "|" : " ")} {Scope}: {Item.Match.Text} {(IsLastInGroup ? "|" : "")}";
}
