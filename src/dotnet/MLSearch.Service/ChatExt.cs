using ActualChat.Localization;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch;

public static class ChatExt
{
    extension(Chat.Chat chat)
    {
        public IndexedGroup ToIndexedGroup(Place? place)
            => new() {
                Id = chat.Id,
                IsPublic = chat.IsPublic && place?.IsPublic != false,
                Title = chat.Title,
                LocalizedTitles = chat.GetLocalizedSystemChatTitles(),
                PlaceId = chat.Id is PlaceChatId placeChatId ? placeChatId.PlaceId : null,
            };

        public IndexedChat ToIndexedChat(Place? place)
            => new (chat.Id) {
                PlaceId = chat.Id is PlaceChatId placeChatId ? placeChatId.PlaceId : null,
                IsPublic = chat.IsPublic,
                IsPublicInPlace = chat.IsPublic && place?.IsPublic != false,
            };
    }
}
