using ActualChat.Chat;
using ActualChat.Search;

namespace ActualChat.MLSearch.IntegrationTests.Util;

public static class IndexedContactUtil
{
    public static ApiArray<IndexedGroupChatContact> BuildChatContacts(IEnumerable<Place> places, params Chat.Chat[] chats)
        => BuildChatContacts(places.ToDictionary(x => x.Id), chats);

    private static ApiArray<IndexedGroupChatContact> BuildChatContacts(
        IReadOnlyDictionary<PlaceId, Place> placeMap,
        Chat.Chat[] chats)
    {
        return Build().ToApiArray();

        IEnumerable<IndexedGroupChatContact> Build()
        {
            foreach (var chat in chats)
                yield return chat.ToIndexedChatContact(placeMap.GetValueOrDefault(chat.Id.PlaceId));
        }
    }
}
