namespace ActualChat.Chat.UI.Blazor.Services;

public class ListeningChatsList
{
    public IMutableState<ImmutableList<string>> ListeningChatsState { get; }
    public ImmutableList<string> ListeningChats => ListeningChatsState.Value;

    public ListeningChatsList(IStateFactory stateFactory)
        => ListeningChatsState = stateFactory.NewMutable(ImmutableList<string>.Empty);

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
