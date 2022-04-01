namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatUI
{
    private Symbol _activeChatId;

    [ComputeMethod]
    public virtual Task<Symbol> GetActiveChatId()
        => Task.FromResult(_activeChatId);

    public void SetActiveChatId(Symbol chatId)
    {
        _activeChatId = chatId;
        using (Computed.Invalidate())
            _ = GetActiveChatId();
    }
}
