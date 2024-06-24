using ActualChat.Chat;

namespace ActualChat.Search.IntegrationTests;

public static class IndexedContactUtil
{
    public static ApiArray<IndexedChatContact> BuildChatContacts(IEnumerable<Place> places, params Chat.Chat[] chats)
        => BuildChatContacts(places.ToDictionary(x => x.Id), chats);

    private static ApiArray<IndexedChatContact> BuildChatContacts(
        IReadOnlyDictionary<PlaceId, Place> placeMap,
        Chat.Chat[] chats)
    {
        return Build().ToApiArray();

        IEnumerable<IndexedChatContact> Build()
        {
            foreach (var chat in chats)
                yield return chat.ToIndexedChatContact(placeMap.GetValueOrDefault(chat.Id.PlaceId));

            foreach (var place in placeMap.Values)
                yield return place.ToIndexedPlaceContact();
        }
    }
}
