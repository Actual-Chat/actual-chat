namespace ActualChat.Chat.UI.Blazor.Services;

public class ListeningChats
{
    public IMutableState<ImmutableList<string>> ChatIdsState { get; }
    public ImmutableList<string> ChatIds => ChatIdsState.Value;

    public ListeningChats(IStateFactory stateFactory)
        => ChatIdsState = stateFactory.NewMutable(ImmutableList<string>.Empty);

    public void Add(string chatId)
    {
        if (!string.IsNullOrWhiteSpace(chatId) && !ChatIds.Contains(chatId, StringComparer.Ordinal))
            ChatIdsState.Value = ChatIds.Add(chatId);
    }

    public void Remove(string chatId)
    {
        if (!string.IsNullOrWhiteSpace(chatId) && ChatIds.Contains(chatId, StringComparer.Ordinal))
            ChatIdsState.Value = ChatIds.Remove(chatId, StringComparer.Ordinal);
    }

    public ValueTask<ImmutableList<string>> GetChatIds(CancellationToken cancellationToken)
        => ChatIdsState.Use(cancellationToken);
}
