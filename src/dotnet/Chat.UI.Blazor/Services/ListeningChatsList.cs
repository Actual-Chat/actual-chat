namespace ActualChat.Chat.UI.Blazor.Services;

public enum ListenChatMode { Active, Muted }

public record ListenChatInfo(Symbol ChatId, ListenChatMode Mode);

public class ListeningChats
{
    private ImmutableList<ListenChatInfo> _chatInfos = ImmutableList<ListenChatInfo>.Empty;
    private readonly object _syncObject = new ();

    public ListeningChats()
    {
    }

    public void Add(Symbol chatId)
        => Set(chatId, ListenChatMode.Active);

    public void Set(Symbol chatId, ListenChatMode mode)
    {
        if (chatId.IsEmpty)
            return;

        lock (_syncObject) {
            var chatInfo = _chatInfos.Find(c => c.ChatId == chatId);
            if (chatInfo == null)
                _chatInfos = _chatInfos.Add(new ListenChatInfo(chatId, mode));
            else if (chatInfo.Mode != mode)
                _chatInfos = _chatInfos.Replace(chatInfo, chatInfo with { Mode = mode });
            else
                return;
        }

        using (Computed.Invalidate())
            _ = GetListenChatInfos();
    }

    public void Remove(Symbol chatId)
    {
        if (chatId.IsEmpty)
            return;

        lock (_syncObject) {
            var chatInfo = _chatInfos.Find(c => c.ChatId == chatId);
            if (chatInfo != null)
                _chatInfos = _chatInfos.Remove(chatInfo);
            else
                return;
        }

        using (Computed.Invalidate())
            _ = GetListenChatInfos();
    }

    [ComputeMethod]
    public virtual Task<ImmutableList<ListenChatInfo>> GetListenChatInfos()
        => Task.FromResult(_chatInfos);

    [ComputeMethod]
    public virtual async Task<ImmutableList<Symbol>> GetChatIds()
        => (await GetListenChatInfos().ConfigureAwait(false)).Select(c => c.ChatId).ToImmutableList();
}
