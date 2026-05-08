namespace ActualChat.UI.Blazor.App.Services;

public enum SearchLocationFilter
{
    Anywhere,
    Chat,
    Place,
}

public enum SearchTypeFilter
{
    Anything,
    People,
    Groups,
    Messages,
}

public sealed record SearchResultGroupKey(ActualChat.Search.SearchScope Scope, bool IsGlobalSearchResult);
