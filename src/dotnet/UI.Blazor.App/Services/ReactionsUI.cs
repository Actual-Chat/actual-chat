using ActualChat.Chat;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Reads reactions with the client queue's not-yet-confirmed ones folded in,
/// so a reaction shows up the moment it's queued rather than when the server answers.
/// </summary>
public class ReactionsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly HashSet<(string EntryId, string EmojiId)> _pendingAnimations = new();
    private readonly ConcurrentDictionary<IQueuedCommand, bool> _isRemoveIntents = new();
    private IReactions Reactions => Hub.Reactions;
    private ClientCommandQueue Queue => Hub.ClientCommandQueue;
    private ClientCommandQueueTriggers Triggers => Hub.ClientCommandQueueTriggers;

    [ComputeMethod]
    public virtual async Task<ReactionsModel?> Get(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        await Triggers.OnChanged(entryId.Value).ConfigureAwait(false);
        var summaries = await Reactions.ListSummaries(Session, entryId, cancellationToken).ConfigureAwait(false);
        var ownReaction = summaries.Length > 0
            ? await Reactions.Get(Session, entryId, cancellationToken).ConfigureAwait(false)
            : null;

        RecordIntents(entryId, ownReaction);
        ConfirmReflected(entryId, ownReaction);
        var pending = GetPending(entryId);
        if (pending.Count == 0)
            return summaries.Length == 0 ? null : new ReactionsModel(summaries, ownReaction);

        var emojis = pending.Select(x => ((Reactions_React)x.Command).Reaction.Emoji).ToArray();
        var model = ReactionsOverlay.Fold(summaries, ownReaction, emojis, entryId);
        return model with { PendingStage = pending[^1].Stage };
    }

    [ComputeMethod]
    public virtual async Task<bool> HasVisible(ChatEntryId entryId, bool hasServerReactions)
    {
        if (hasServerReactions)
            return true;

        await Triggers.OnChanged(entryId.Value).ConfigureAwait(false);
        return GetPending(entryId).Count > 0;
    }

    public void AddPendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Add((entryId, emojiId));

    public bool RemovePendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Remove((entryId, emojiId));

    // Private methods

    private void RecordIntents(ChatEntryId entryId, Reaction? serverOwnReaction)
    {
        // Reactions_React carries no intent - the server toggles - so what a command meant is
        // only knowable from the state it was queued against. The first observation wins;
        // by then the server hasn't applied it yet, which is exactly the state we need.
        var own = serverOwnReaction;
        foreach (var entry in Queue.GetEntries(entryId.Value)) {
            if (entry.Command is not Reactions_React react)
                continue;

            var emoji = react.Reaction.Emoji;
            var isRemove = own?.Emoji == emoji;
            _isRemoveIntents.TryAdd(entry.Command, isRemove);
            own = isRemove
                ? null
                : new Reaction { Id = Symbol.Empty, AuthorId = default!, EntryId = entryId, Emoji = emoji };
        }
    }

    private void ConfirmReflected(ChatEntryId entryId, Reaction? ownReaction)
    {
        // Confirming here rather than on completion keeps the effect from blinking
        // between the command finishing and its invalidation reaching the UI.
        foreach (var entry in Queue.GetEntries(entryId.Value)) {
            if (entry.Command is not Reactions_React react)
                continue;
            if (entry.Stage != QueuedCommandStage.Completed)
                continue;

            // A pending remove is reflected when the server no longer shows that emoji
            var isRemove = _isRemoveIntents.GetValueOrDefault(entry.Command);
            var isReflected = ReactionsOverlay.IsReflected(ownReaction, react.Reaction.Emoji) != isRemove;
            if (!isReflected)
                continue;

            Queue.Confirm(entry.Command);
            _isRemoveIntents.TryRemove(entry.Command, out _);
        }
    }

    private IReadOnlyList<QueuedCommandEntry> GetPending(ChatEntryId entryId)
        => Queue.GetEntries(entryId.Value)
            .Where(x => x is { Stage: not QueuedCommandStage.Failed, Command: Reactions_React })
            .ToArray();
}
