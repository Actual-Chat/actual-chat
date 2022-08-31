namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessagesFactory
{
    private Session Session { get; }
    private IChats Chats { get; }
    private ChatUI ChatUI { get; }

    public UnreadMessagesFactory(Session session, IChats chats, ChatUI chatUI)
    {
        Session = session;
        Chats = chats;
        ChatUI = chatUI;
    }

    public UnreadMessages Get(Symbol chatId)
        => new (Session, chatId, ChatUI, Chats);
}
