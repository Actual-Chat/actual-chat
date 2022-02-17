using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatAuthorListeningChats
{
    private IChats Chats { get; set; } = null!;
    private Session Session { get; }
    public List<Chat> ListeningChats { get; set; } = new ();

    public ChatAuthorListeningChats(IChats chats, Session session)
    {
        Chats = chats;
        Session = session;
    }

    public async Task AddChat(string chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat != null && !ListeningChats.Contains(chat))
            ListeningChats.Add(chat);
    }

    public async Task RemoveChat(string chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat != null && ListeningChats.Contains(chat))
            ListeningChats.Remove(chat);
    }
}
