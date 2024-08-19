using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Components;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ChatContext(ChatUIHub hub, Chat.Chat chat, AccountFull ownAccount)
{
    private IChatMarkupHub? _chatMarkupHub;

    public ChatUIHub Hub {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => hub;
    }

    public Chat.Chat Chat {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => chat;
    }

    public AccountFull OwnAccount => ownAccount;
    public bool HasChat => !Chat.Id.IsNone;
    public IChatMarkupHub ChatMarkupHub => GetChatMarkupHub();

    // Some handy helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChatEntryReader NewEntryReader(ChatEntryKind entryKind)
        => new(Hub.Chats, Hub.Session(), Chat.Id, entryKind);

    // Private methods

    private IChatMarkupHub GetChatMarkupHub()
    {
        var chatMarkupHub = _chatMarkupHub;
        return chatMarkupHub != null && chatMarkupHub.ChatId == Chat.Id
            ? chatMarkupHub
            : _chatMarkupHub = Hub.ChatMarkupHubFactory[Chat.Id];
    }
}
