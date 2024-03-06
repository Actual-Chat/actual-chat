using ActualChat.Chat;

namespace ActualChat.Search;

public static class ChatExt
{
    public static IndexedChatContact ToIndexedChatContact(this Chat.Chat chat, Place? place)
        => new() {
            Id = chat.Id,
            IsPublic = chat.IsPublic && place?.IsPublic != false,
            Title = chat.Title,
            PlaceId = chat.Id.PlaceId,
        };
}
