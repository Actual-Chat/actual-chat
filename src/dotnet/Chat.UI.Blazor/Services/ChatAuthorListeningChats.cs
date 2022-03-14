namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatAuthorListeningChats
{
    private Session Session { get; }
    public IServiceProvider Services { get; }
    public IMutableState<ImmutableList<string>> ListeningChatsState { get; }
    public ImmutableList<string> ListeningChats => ListeningChatsState.Value;

    public ChatAuthorListeningChats(IServiceProvider services, Session session)
    {
        Session = session;
        Services = services;
        var stateFactory = services.StateFactory();
        ListeningChatsState = stateFactory.NewMutable(ImmutableList<string>.Empty);
    }

    public void Add(string chatId)
    {
        if (!string.IsNullOrWhiteSpace(chatId) && !ListeningChats.Contains(chatId, StringComparer.Ordinal))
            ListeningChatsState.Value = ListeningChats.Add(chatId);
    }

    public void Remove(string chatId)
    {
        if (!string.IsNullOrWhiteSpace(chatId) && ListeningChats.Contains(chatId, StringComparer.Ordinal))
            ListeningChatsState.Value = ListeningChats.Remove(chatId, StringComparer.Ordinal);
    }

    public ValueTask<ImmutableList<string>> GetChatIds(CancellationToken cancellationToken)
        => ListeningChatsState.Use(cancellationToken);
}
