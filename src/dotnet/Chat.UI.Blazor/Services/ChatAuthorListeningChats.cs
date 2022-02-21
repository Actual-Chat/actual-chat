using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatAuthorListeningChats
{
    private Session Session { get; }
    public List<string> ListeningChatIds { get; set; } = new ();

    public ChatAuthorListeningChats(Session session)
    {
        Session = session;
    }

    public Task AddChat(string chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || ListeningChatIds.Contains(chatId, StringComparer.Ordinal))
            return Task.CompletedTask;
        ListeningChatIds.Add(chatId);
        Updated?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task RemoveChat(string chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId) || !ListeningChatIds.Contains(chatId, StringComparer.Ordinal))
            return Task.CompletedTask;
        ListeningChatIds.Remove(chatId);
        Updated?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public event EventHandler? Updated;
}
