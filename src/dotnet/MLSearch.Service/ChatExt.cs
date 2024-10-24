using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.Search;

namespace ActualChat.MLSearch;

public static class ChatExt
{
    public static IndexedGroupChatContact ToIndexedChatContact(this Chat.Chat chat, Place? place)
        => new() {
            Id = chat.Id,
            IsPublic = chat.IsPublic && place?.IsPublic != false,
            Title = chat.Title,
            PlaceId = chat.Id.PlaceId,
        };

    public static IndexedChat ToIndexedChat(this Chat.Chat chat, Place? place)
        => new (chat.Id) {
            PlaceId = chat.Id.PlaceId,
            IsPublic = chat.IsPublic,
            IsPublicInPlace = chat.IsPublic && place?.IsPublic != false,
        };
}
