namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessagesFactory
{
    private Session Session { get; }
    private IChats Chats { get; }
    private ChatUI ChatUI { get; }
    private IMentions Mentions { get; }

    public UnreadMessagesFactory(Session session, IChats chats, ChatUI chatUI, IMentions mentions)
    {
        Session = session;
        Chats = chats;
        ChatUI = chatUI;
        Mentions = mentions;
    }

    public ChatUnreadMessages Get(Symbol chatId)
        => new (Session, chatId, ChatUI, Chats, Mentions);
}
