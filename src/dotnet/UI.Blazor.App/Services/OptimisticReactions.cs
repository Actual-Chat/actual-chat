namespace ActualChat.UI.Blazor.App.Services;

public sealed class OptimisticReactions
{
    private readonly ConcurrentDictionary<ChatEntryId, OptimisticReactionInfo> _pending = new();

    public event Action<ChatEntryId>? Changed;

    public bool HasPendingAdd(ChatEntryId entryId)
        => _pending.TryGetValue(entryId, out var r) && !r.IsRemove;

    public bool TryGet(ChatEntryId entryId, out OptimisticReactionInfo info)
        => _pending.TryGetValue(entryId, out info);

    public void Set(ChatEntryId entryId, Emoji emoji, bool isRemove) {
        _pending[entryId] = new(emoji, isRemove);
        Changed?.Invoke(entryId);
    }

    public void TryRemove(ChatEntryId entryId)
        => _pending.TryRemove(entryId, out _);

    public readonly record struct OptimisticReactionInfo(Emoji Emoji, bool IsRemove);
}
