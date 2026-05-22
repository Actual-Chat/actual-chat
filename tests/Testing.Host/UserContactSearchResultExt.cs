using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Testing.Host;

public static class UserContactSearchResultExt
{
    public static List<FoundItem> BuildFoundContacts(this Account owner, bool areGlobalSearchResults, params IEnumerable<AccountFull> others)
        => others.Select(x => owner.BuildFoundContact(x, areGlobalSearchResults)).ToList();

    public static FoundItem BuildFoundContact(this Account owner, AccountFull other, bool isGlobalSearchResult)
        => new (owner.BuildSearchResult(other), SearchScope.People, isGlobalSearchResult);

    public static List<FoundContact> BuildSearchResults(this Account owner, params IEnumerable<AccountFull> others)
        => others.Select(x => owner.BuildSearchResult(x)).ToList();

    public static FoundContact BuildSearchResult(this Account owner, AccountFull other, Range<int>[]? searchMatchPartRanges = null)
        => owner.Id.BuildSearchResult(other.Id, other.Name, searchMatchPartRanges);

    public static FoundContact BuildSearchResult(
        this UserId ownerId,
        UserId otherUserId,
        string title,
        Range<int>[]? searchMatchPartRanges = null)
        => new (ContactId.NewUser(ownerId, otherUserId), searchMatchPartRanges.BuildSearchMatch(title));
}
