using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ChatContext(AppUIHub hub, ChatId chatId)
{
    private IChatMarkupHub? _chatMarkupHub;

    public AppUIHub Hub {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => hub;
    }

    public ChatId ChatId {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => chatId;
    }

    public IChatMarkupHub ChatMarkupHub => GetChatMarkupHub();

    // Private methods

    private IChatMarkupHub GetChatMarkupHub()
    {
        var chatMarkupHub = _chatMarkupHub;
        return chatMarkupHub != null && chatMarkupHub.ChatId == ChatId
            ? chatMarkupHub
            : _chatMarkupHub = Hub.ChatMarkupHubFactory[ChatId];
    }
}
