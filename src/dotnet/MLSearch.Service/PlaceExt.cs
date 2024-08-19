using ActualChat.Chat;
using ActualChat.Search;

namespace ActualChat.MLSearch;

public static class PlaceExt
{
    public static IndexedPlaceContact ToIndexedPlaceContact(this Place place)
        => new() {
            Id = place.Id,
            IsPublic = place.IsPublic,
            Title = place.Title,
        };
}
