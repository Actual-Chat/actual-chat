namespace ActualChat.Search;

public static class ChatExt
{
    public static IndexedChatContact ToIndexedChatContact(this Chat.Chat chat)
        => new() {
            Id = chat.Id,
            IsPublic = chat.IsPublic,
            Title = chat.Title,
            PlaceId = chat.Id.PlaceId,
        };
}
