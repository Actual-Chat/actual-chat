namespace ActualChat.UI.Blazor.App.Services;

public sealed class OptimisticReactions
{
    private readonly ConcurrentDictionary<ChatEntryId, OptimisticReactionInfo> _pending = new();
    private readonly HashSet<(string EntryId, string EmojiId)> _pendingAnimations = new();

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

    public void AddPendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Add((entryId, emojiId));

    public bool RemovePendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Remove((entryId, emojiId));

    public readonly record struct OptimisticReactionInfo(Emoji Emoji, bool IsRemove);
}
