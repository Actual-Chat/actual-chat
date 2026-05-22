using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Testing.Host;

public static class PlaceContactSearchResultExt
{
    public static List<FoundItem> BuildFoundContacts(
        this Account owner,
        bool areGlobalSearchResults,
        params IEnumerable<Place> places)
        => places.Select(x => owner.BuildFoundContact(x, areGlobalSearchResults)).ToList();

    public static FoundItem BuildFoundContact(this Account owner, Place place, bool isGlobalSearchResult)
        => new (owner.BuildSearchResult(place), SearchScope.Places, isGlobalSearchResult);

    public static List<FoundContact> BuildSearchResults(this Account owner, params IEnumerable<Place> places)
        => places.Select(x => owner.BuildSearchResult(x)).ToList();

    public static FoundContact BuildSearchResult(this Account owner, Place place, string testIsolationKey = "", Range<int>[]? searchMatchPartRanges = null)
        => place.BuildSearchResult(owner.Id, testIsolationKey, searchMatchPartRanges);

    public static FoundContact BuildSearchResult(this Place place, UserId userId, string testIsolationKey = "", Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(userId, place.Id, place.Title, testIsolationKey, searchMatchPartRanges);

    public static FoundContact BuildSearchResult(this UserId ownerId, PlaceId placeId, string title, string testIsolationKey = "", Range<int>[]? searchMatchPartRanges = null)
        => new (ContactId.NewPlace(ownerId, placeId), searchMatchPartRanges.BuildSearchMatch(title, testIsolationKey));
}
