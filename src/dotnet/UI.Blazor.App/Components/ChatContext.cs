using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ChatContext(AppUIHub hub, Chat.Chat chat)
{
    private IChatMarkupHub? _chatMarkupHub;

    public AppUIHub Hub {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => hub;
    }

    public Chat.Chat Chat {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => chat;
    }

    public IChatMarkupHub ChatMarkupHub => GetChatMarkupHub();

    // Private methods

    private IChatMarkupHub GetChatMarkupHub()
    {
        var chatMarkupHub = _chatMarkupHub;
        return chatMarkupHub != null && chatMarkupHub.ChatId == Chat.Id
            ? chatMarkupHub
            : _chatMarkupHub = Hub.ChatMarkupHubFactory[Chat.Id];
    }
}
