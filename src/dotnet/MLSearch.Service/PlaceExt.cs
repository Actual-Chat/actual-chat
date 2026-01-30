using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch;

public static class PlaceExt
{
    public static IndexedPlace ToIndexedPlaceContact(this Place place)
        => new() {
            Id = place.Id,
            IsPublic = place.IsPublic,
            Title = place.Title,
        };
}
