using ActualChat.Search;
using ActualChat.Users;
using FluentAssertions.Equivalency;
using ActualLab.Versioning;

namespace ActualChat.Testing.Assertion;

public static class AssertOptionsExt
{
    public static EquivalencyAssertionOptions<AccountFull> IdName(
        this EquivalencyAssertionOptions<AccountFull> options)
        => options.Including(x => x.Id).Including(x => x.FullName).Including(x => x.User.Name);

    public static EquivalencyAssertionOptions<T> ExcludingSystemProperties<T>(
        this EquivalencyAssertionOptions<T> options) where T : notnull
        => options.Excluding(mi => OrdinalEquals(mi.Name, nameof(IHasVersion<T>.Version)))
            .Excluding(mi => OrdinalEquals(mi.Name, "CreatedAt"))
            .Excluding(mi => OrdinalEquals(mi.Name, "ModifiedAt"));

    public static EquivalencyAssertionOptions<ContactSearchResult> ExcludingRank(
        this EquivalencyAssertionOptions<ContactSearchResult> options)
        => options.Excluding(x => x.SearchMatch.Rank)
            .For(x => x.SearchMatch.Parts)
            .Exclude(x => x.Rank);

    public static EquivalencyAssertionOptions<ContactSearchResult> ExcludingSearchMatch(
        this EquivalencyAssertionOptions<ContactSearchResult> options)
        => options.Excluding(x => x.SearchMatch);
}
