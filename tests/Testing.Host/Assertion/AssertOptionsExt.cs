using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Versioning;
using AwesomeAssertions.Equivalency;

namespace ActualChat.Testing.Host.Assertion;

public static class AssertOptionsExt
{
    public static EquivalencyOptions<Chat.Chat> IdTitle(
        this EquivalencyOptions<Chat.Chat> options)
        => options.Including(x => x.Id).Including(x => x.Title);

    public static EquivalencyOptions<T> ExcludingSystemProperties<T>(
        this EquivalencyOptions<T> options) where T : notnull
        => options.Excluding(mi => mi.Name == nameof(IHasVersion<T>.Version))
            .Excluding(mi => mi.Name == "CreatedAt")
            .Excluding(mi => mi.Name == "ModifiedAt");

    public static EquivalencyOptions<FoundContact> ExcludingRank(
        this EquivalencyOptions<FoundContact> options)
        => options.Excluding(x => x.Match.Rank)
            .For(x => x.Match.Parts)
            .Exclude(x => x.Rank);

    public static EquivalencyOptions<FoundContact> ExcludingUniquePart(
        this EquivalencyOptions<FoundContact> options)
        => options.Excluding(x => x.Match.Rank)
            .For(x => x.Match.Parts)
            .Exclude(x => x.Rank);

    public static EquivalencyOptions<FoundContact> ExcludingSearchMatch(
        this EquivalencyOptions<FoundContact> options)
        => options.Excluding(x => x.Match);

    public static EquivalencyOptions<FoundChatEntry> ExcludingSearchMatch(
        this EquivalencyOptions<FoundChatEntry> options)
        => options.Excluding(x => x.Match);

    public static EquivalencyOptions<Notifications.Notification> Text(
        this EquivalencyOptions<Notifications.Notification> options)
        => options.Including(x => x.Title).Including(x => x.Text);

    public static EquivalencyOptions<FoundItem> ExcludingSearchMatch(
        this EquivalencyOptions<FoundItem> options)
        => options.Excluding(x => x.Item.Match)
            .Excluding(x => x.ContactSearchMatch)
            .Excluding(x => x.ChatEntrySearchMatch);

    public static EquivalencyOptions<FoundItem> ExcludingBorders(
        this EquivalencyOptions<FoundItem> options)
        => options.Excluding(x => x.IsFirstInGroup)
            .Excluding(x => x.IsLastInGroup)
            .Excluding(x => x.CanScopeBeExpanded);
}
