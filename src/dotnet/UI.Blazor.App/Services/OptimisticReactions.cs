using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class OptimisticReactions(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private readonly ConcurrentDictionary<(ChatEntryId EntryId, Emoji Emoji), bool> _pending = new();
    private readonly HashSet<(string EntryId, string EmojiId)> _pendingAnimations = new();

    private IReactions Reactions => Hub.Reactions;

    public event Action<ChatEntryId>? Changed;

    public async Task Toggle(ChatEntry entry, Emoji emoji)
    {
        _ = TuneUI.Play(Tune.React);
        var entryId = entry.Id;
        Reaction[] ownReactions = [];
        if (entry.HasReactions)
            ownReactions = await Reactions.ListOwn(Session, entryId, CancellationToken.None).ConfigureAwait(true);
        var ownEmojis = ownReactions.Select(x => x.Emoji).ToHashSet();
        foreach (var pending in List(entryId))
            if (pending.IsRemove)
                ownEmojis.Remove(pending.Emoji);
            else
                ownEmojis.Add(pending.Emoji);

        var isRemove = ownEmojis.Contains(emoji);
        if (!isRemove)
            _pendingAnimations.Add((entryId.Value, emoji.Id.Value));
        _pending[(entryId, emoji)] = isRemove;
        Changed?.Invoke(entryId);

        var reaction = new Reaction {
            Id = Symbol.Empty,
            AuthorId = null!,
            EntryId = entryId,
            Emoji = emoji,
        };
        var reactCmd = new Reactions_React { Session = Session, Reaction = reaction };
        await UICommander.Run(reactCmd).ConfigureAwait(true);
    }

    public bool HasPendingAdd(ChatEntryId entryId)
        => _pending.Any(x => x.Key.EntryId == entryId && !x.Value);

    public List<OptimisticReactionInfo> List(ChatEntryId entryId)
        => _pending
            .Where(x => x.Key.EntryId == entryId)
            .Select(x => new OptimisticReactionInfo(x.Key.Emoji, x.Value))
            .ToList();

    public void TryRemove(ChatEntryId entryId, Emoji emoji)
        => _pending.TryRemove((entryId, emoji), out _);

    public bool RemovePendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Remove((entryId, emojiId));

    // Nested types

    public readonly record struct OptimisticReactionInfo(Emoji Emoji, bool IsRemove);
}
