namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatPageState
{
    private readonly object _lock = new();
    private Symbol _activeChatId;

    [ComputeMethod]
    public virtual Task<Symbol> GetActiveChatId()
    {
        lock (_lock)
            return Task.FromResult(_activeChatId);
    }

    public void SetActiveChatId(Symbol chatId)
    {
        lock (_lock)
            _activeChatId = chatId;
        using (Computed.Invalidate())
            _ = GetActiveChatId();
    }

    public void ResetActiveChatId(Symbol expectedChatId)
    {
        lock (_lock) {
            if (_activeChatId != expectedChatId)
                return;
            _activeChatId = Symbol.Empty;
        }
        using (Computed.Invalidate())
            _ = GetActiveChatId();
    }
}
