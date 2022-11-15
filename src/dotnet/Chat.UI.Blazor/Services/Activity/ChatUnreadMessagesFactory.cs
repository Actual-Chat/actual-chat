namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatUnreadMessagesFactory
{
    private Session Session { get; }
    private IChats Chats { get; }
    private IMentions Mentions { get; }
    private ChatUI ChatUI { get; }

    public ChatUnreadMessagesFactory(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Chats = services.GetRequiredService<IChats>();
        Mentions = services.GetRequiredService<IMentions>();
        ChatUI = services.GetRequiredService<ChatUI>();
    }

    public ChatUnreadMessages Get(ChatId chatId)
        => new(Session, chatId, Chats, Mentions, ChatUI);
}
