using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;
using ActualLab.Mathematics;

namespace ActualChat.Testing.Host;

public static class PlaceContactSearchResultUtil
{
    public static IEnumerable<FoundItem> BuildFoundContacts(
        this Account owner,
        bool areGlobalSearchResults,
        params Place[] places)
        => places.Select(x => owner.BuildFoundContact(x, areGlobalSearchResults));

    public static FoundItem BuildFoundContact(this Account owner, Place place, bool isGlobalSearchResult)
        => new (owner.BuildSearchResult(place), SearchScope.Places, isGlobalSearchResult);

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params Place[] places)
        => places.Select(x => owner.BuildSearchResult(x));

    public static ContactSearchResult BuildSearchResult(this Account owner, Place place, string uniquePart = "", Range<int>[]? searchMatchPartRanges = null)
        => place.BuildSearchResult(owner.Id, uniquePart, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this Place place, UserId userId, string uniquePart = "", Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(userId, place.Id, place.Title, uniquePart, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this UserId ownerId, PlaceId placeId, string title, string uniquePart = "", Range<int>[]? searchMatchPartRanges = null)
        => new (new ContactId(ownerId, placeId.ToRootChatId()), searchMatchPartRanges.BuildSearchMatch(title, uniquePart));
}
