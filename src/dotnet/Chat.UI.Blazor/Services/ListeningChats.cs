namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatListeningMode { Active, Muted }

[StructLayout(LayoutKind.Auto)]
public record struct ChatListeningInfo(Symbol ChatId, ChatListeningMode Mode);

// ReSharper disable once ClassNeverInstantiated.Global
public class ListeningChats
{
    private volatile ImmutableList<ChatListeningInfo> _chats = ImmutableList<ChatListeningInfo>.Empty;
    private readonly object _lock = new();

    [ComputeMethod]
    public virtual Task<ImmutableList<ChatListeningInfo>> GetChats(CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(_chats);
    }

    [ComputeMethod]
    public virtual async Task<ImmutableList<Symbol>> GetChatIds(CancellationToken cancellationToken)
        => (await GetChats(cancellationToken).ConfigureAwait(false)).Select(c => c.ChatId).ToImmutableList();

    public void Set(Symbol chatId, ChatListeningMode mode)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        lock (_lock) {
            var info = _chats.Find(c => c.ChatId == chatId);
            if (info.ChatId.IsEmpty)
                _chats = _chats.Add(new ChatListeningInfo(chatId, mode));
            else if (info.Mode != mode)
                _chats = _chats.Replace(info, info with { Mode = mode });
            else
                return;
        }

        using (Computed.Invalidate())
            _ = GetChats(default);
    }

    public void Remove(Symbol chatId)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        lock (_lock) {
            var info = _chats.Find(c => c.ChatId == chatId);
            if (info.ChatId.IsEmpty)
                return;
            _chats = _chats.Remove(info);
        }

        using (Computed.Invalidate())
            _ = GetChats(default);
    }
}
