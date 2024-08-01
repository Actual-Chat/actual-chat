using ActualChat.Search;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed record FoundContact(
    ContactSearchResult SearchResult,
    ContactSearchScope Scope,
    bool IsFirstInGroup = false,
    bool IsLastInGroup = false,
    bool CanScopeBeExpanded = false)
{
    public override string ToString()
        => $"{(IsFirstInGroup ? "|" : " ")} {Scope}: {SearchResult.Text} {(IsLastInGroup ? "|" : "")}";
}
