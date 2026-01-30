using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch;

public static class ChatExt
{
    public static IndexedGroup ToIndexedGroup(this Chat.Chat chat, Place? place)
        => new() {
            Id = chat.Id,
            IsPublic = chat.IsPublic && place?.IsPublic != false,
            Title = chat.Title,
            PlaceId = chat.Id is PlaceChatId placeChatId ? placeChatId.PlaceId : null,
        };

    public static IndexedChat ToIndexedChat(this Chat.Chat chat, Place? place)
        => new (chat.Id) {
            PlaceId = chat.Id is PlaceChatId placeChatId ? placeChatId.PlaceId : null,
            IsPublic = chat.IsPublic,
            IsPublicInPlace = chat.IsPublic && place?.IsPublic != false,
        };
}
