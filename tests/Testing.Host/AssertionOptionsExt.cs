using ActualChat.UI.Blazor.App.Services;
using FluentAssertions.Equivalency;

namespace ActualChat.Testing.Host;

public static class AssertionOptionsExt
{
    public static EquivalencyAssertionOptions<FoundContact> ExcludingSearchMatch(
        this EquivalencyAssertionOptions<FoundContact> options)
        => options.Excluding(x => x.SearchResult.SearchMatch);

    public static EquivalencyAssertionOptions<FoundContact> ExcludingBorders(
        this EquivalencyAssertionOptions<FoundContact> options)
        => options.Excluding(x => x.IsFirstInGroup)
            .Excluding(x => x.IsLastInGroup)
            .Excluding(x => x.CanScopeBeExpanded);
}
