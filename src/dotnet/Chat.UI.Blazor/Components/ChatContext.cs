using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record ChatContext(
    IServiceProvider Services,
    Session Session,
    Chat Chat,
    AccountFull OwnAccount
    ) : ChatHub(Services, Session)
{
    private IChatMarkupHub? _chatMarkupHub;

    public bool HasChat => !Chat.Id.IsNone;

    public IChatMarkupHub ChatMarkupHub => GetChatMarkupHub();

    public static ChatContext New(
        IServiceProvider services,
        Session session,
        Chat chat,
        AccountFull ownAccount,
        ChatContext? lastContext)
    {
        // This method saves on allocation + service resolution
        // by reusing as much as possible from the lastContext
        if (lastContext == null)
            return new ChatContext(services, session, chat, ownAccount);

        // Technically there is Session too, but Session is "pinned" to scoped Services,
        // so no need to compare it.
        var mustReset =
            !ReferenceEquals(services, lastContext.Services) // Services changed
            || !ReferenceEquals(ownAccount, lastContext.OwnAccount); // Own account changed
        if (mustReset)
            return new ChatContext(services, session, chat, ownAccount);

        // Maybe chat is unchanged, but we intentionally return a new context here,
        // coz the this method is called in ChatPage.ComputeState, which is invoked
        // when ChatPage's parameters are set, and we want to trigger parameter change
        // on all child components in this case - in particular, to make sure
        // ChatView.TryNavigateToEntry is called.
        // It's important though that cached service set stays the same in ChatContext
        // in this case.
        return lastContext with { Chat = chat };
    }

    // This record relies on referential equality
    public bool Equals(ChatContext? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);

    // Private methods

    private IChatMarkupHub GetChatMarkupHub()
    {
        var chatMarkupHub = _chatMarkupHub;
        return chatMarkupHub != null && chatMarkupHub.ChatId == Chat.Id
            ? chatMarkupHub
            : _chatMarkupHub = ChatMarkupHubFactory[Chat.Id];
    }
}
