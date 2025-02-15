using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Versioning;
using FluentAssertions.Equivalency;

namespace ActualChat.Testing.Host.Assertion;

public static class AssertOptionsExt
{
    public static EquivalencyOptions<Chat.Chat> IdTitle(
        this EquivalencyOptions<Chat.Chat> options)
        => options.Including(x => x.Id).Including(x => x.Title);

    public static EquivalencyOptions<T> ExcludingSystemProperties<T>(
        this EquivalencyOptions<T> options) where T : notnull
        => options.Excluding(mi => OrdinalEquals(mi.Name, nameof(IHasVersion<T>.Version)))
            .Excluding(mi => OrdinalEquals(mi.Name, "CreatedAt"))
            .Excluding(mi => OrdinalEquals(mi.Name, "ModifiedAt"));

    public static EquivalencyOptions<ContactSearchResult> ExcludingRank(
        this EquivalencyOptions<ContactSearchResult> options)
        => options.Excluding(x => x.SearchMatch.Rank)
            .For(x => x.SearchMatch.Parts)
            .Exclude(x => x.Rank);

    public static EquivalencyOptions<ContactSearchResult> ExcludingUniquePart(
        this EquivalencyOptions<ContactSearchResult> options)
        => options.Excluding(x => x.SearchMatch.Rank)
            .For(x => x.SearchMatch.Parts)
            .Exclude(x => x.Rank);

    public static EquivalencyOptions<ContactSearchResult> ExcludingSearchMatch(
        this EquivalencyOptions<ContactSearchResult> options)
        => options.Excluding(x => x.SearchMatch);

    public static EquivalencyOptions<EntrySearchResult> ExcludingSearchMatch(
        this EquivalencyOptions<EntrySearchResult> options)
        => options.Excluding(x => x.SearchMatch);

    public static EquivalencyOptions<Notification.Notification> Text(
        this EquivalencyOptions<Notification.Notification> options)
        => options.Including(x => x.Title).Including(x => x.Content);

    public static EquivalencyOptions<FoundItem> ExcludingSearchMatch(
        this EquivalencyOptions<FoundItem> options)
        => options.Excluding(x => x.SearchResult.SearchMatch)
            .Excluding(x => x.ContactSearchMatch)
            .Excluding(x => x.MessageSearchMatch);

    public static EquivalencyOptions<FoundItem> ExcludingBorders(
        this EquivalencyOptions<FoundItem> options)
        => options.Excluding(x => x.IsFirstInGroup)
            .Excluding(x => x.IsLastInGroup)
            .Excluding(x => x.CanScopeBeExpanded);
}
