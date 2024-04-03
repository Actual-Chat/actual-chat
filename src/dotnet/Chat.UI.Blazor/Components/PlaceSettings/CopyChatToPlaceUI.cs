using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public static class CopyChatToPlaceUI
{
    public static Task CopyChat(
        ChatUIHub hub,
        ChatId sourceChatId,
        PlaceId placeId)
    {
        return Task.CompletedTask;
    }
}
