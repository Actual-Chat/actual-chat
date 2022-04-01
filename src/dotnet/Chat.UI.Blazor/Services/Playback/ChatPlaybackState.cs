namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatPlaybackMode { None = 0, Realtime, RealtimeMuted, Historical }

[StructLayout(LayoutKind.Auto)]
public record struct ChatPlaybackInfo(Symbol ChatId, ChatPlaybackMode Mode);

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatPlaybackState
{
    private volatile ImmutableList<ChatPlaybackInfo> _list = ImmutableList<ChatPlaybackInfo>.Empty;
    private readonly object _lock = new();

    public ImmutableList<ChatPlaybackInfo> List => _list;
    public ChatPlaybackMode this[Symbol chatId] => _list.Find(c => c.ChatId == chatId).Mode;

    [ComputeMethod]
    public virtual Task<ChatPlaybackMode> GetMode(Symbol chatId, CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(this[chatId]);
    }

    [ComputeMethod]
    public virtual Task<ImmutableList<ChatPlaybackInfo>> GetList(CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(_list);
    }

    [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> GetChatIds(CancellationToken cancellationToken)
        => (await GetList(cancellationToken).ConfigureAwait(false)).Select(c => c.ChatId).ToImmutableArray();

    // This method should be called only from ChatPlayers
    public void SetMode(Symbol chatId, ChatPlaybackMode mode)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        lock (_lock) {
            var info = _list.Find(c => c.ChatId == chatId);
            if (info.Mode is ChatPlaybackMode.None) {
                if (mode is not ChatPlaybackMode.None)
                    _list = _list.Add(new ChatPlaybackInfo(chatId, mode));
            }
            else {
                if (info.Mode == mode)
                    return;
                _list = mode is not ChatPlaybackMode.None
                    ? _list.Replace(info, info with { Mode = mode })
                    : _list.Remove(info);
            }
        }
        using (Computed.Invalidate()) {
            _ = GetMode(chatId, default);
            _ = GetList(default);
        }
    }
}
