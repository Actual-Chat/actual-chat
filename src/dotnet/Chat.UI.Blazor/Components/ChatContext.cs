using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ChatContext(ChatUIHub hub, Chat chat, AccountFull ownAccount)
{
    private IChatMarkupHub? _chatMarkupHub;

    public ChatUIHub Hub {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => hub;
    }

    public Chat Chat {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => chat;
    }

    public AccountFull OwnAccount => ownAccount;
    public bool HasChat => !Chat.Id.IsNone;
    public IChatMarkupHub ChatMarkupHub => GetChatMarkupHub();

    // Some handy helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChatEntryReader NewEntryReader(ChatEntryKind entryKind, TileLayer<long>? idTileLayer = null)
        => new(Hub.Chats, Hub.Session(), Chat.Id, entryKind, idTileLayer);

    // Private methods

    private IChatMarkupHub GetChatMarkupHub()
    {
        var chatMarkupHub = _chatMarkupHub;
        return chatMarkupHub != null && chatMarkupHub.ChatId == Chat.Id
            ? chatMarkupHub
            : _chatMarkupHub = Hub.ChatMarkupHubFactory[Chat.Id];
    }
}
