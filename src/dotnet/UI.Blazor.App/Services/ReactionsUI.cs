using ActualChat.Chat;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Reads reactions with the not-yet-confirmed queued ones folded in,
/// so a reaction shows up the moment it's queued rather than when the server answers.
/// </summary>
public class ReactionsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly HashSet<(string EntryId, string EmojiId)> _pendingAnimations = new();
    private IReactions Reactions => Hub.Reactions;
    private CommandQueueUI CommandQueueUI => Hub.CommandQueueUI;

    [ComputeMethod]
    public virtual async Task<ReactionsModel?> Get(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        await CommandQueueUI.OnChanged(entryId.Value).ConfigureAwait(false);
        var summaries = await Reactions.ListSummaries(Session, entryId, cancellationToken).ConfigureAwait(false);
        Reaction? ownReaction = null;
        if (summaries.Length > 0)
            ownReaction = await Reactions.Get(Session, entryId, cancellationToken).ConfigureAwait(false);

        ConfirmReflected(entryId, ownReaction);
        var entries = GetEntries(entryId);
        var lastStage = entries.Count > 0 ? entries[^1].Stage : (QueuedCommandStage?)null;
        if (GetLastPending(entries) is not { } lastPending) {
            // Only failed commands are left, if any: the server state is the truth,
            // and the row is marked with the reaction that didn't get through
            return summaries.Length == 0 ? null : new ReactionsModel(summaries, ownReaction, lastStage);
        }

        var model = ReactionsOverlay.Fold(summaries, ownReaction, lastPending);
        // A queued removal of the last reaction leaves nothing to show, and the row renders
        // its add button even with no badges in it - an empty strip under the message
        return model.IsEmpty ? null : model with { PendingStage = lastStage };
    }

    [ComputeMethod]
    public virtual async Task<bool> HasVisible(
        ChatEntryId entryId,
        bool hasServerReactions,
        CancellationToken cancellationToken)
    {
        await CommandQueueUI.OnChanged(entryId.Value).ConfigureAwait(false);
        if (GetLastPending(GetEntries(entryId)) is null) {
            // Nothing queued over the server state, so the server state alone decides -
            // and asking it here would cost a ListSummaries call per message on screen
            return hasServerReactions;
        }

        var model = await Get(entryId, cancellationToken).ConfigureAwait(false);
        return model is not null;
    }

    public void React(ChatEntryId entryId, Emoji emoji)
    {
        _ = TuneUI.Play(Tune.React);
        var mustRemove = GetVisibleOwnEmoji(entryId) == emoji;
        if (!mustRemove) {
            _pendingAnimations.Add((entryId.Value, emoji.Id.Value));
            // Ranking is a private preference: it must neither delay the command nor,
            // if the queue is holding one, wait for it to reach the server
            _ = UserSettingsUI.UserReactionSettings()
                .Update(s => s.WithBubbleUp(emoji.Id.Value), CancellationToken.None);
        }

        var reaction = new Reaction {
            Id = Symbol.Empty,
            AuthorId = null!,
            EntryId = entryId,
            Emoji = emoji,
        };
        var reactCmd = new Reactions_React { Session = Session, Reaction = reaction, MustRemove = mustRemove };
        CommandQueueUI.Enqueue(reactCmd);
    }

    public bool RemovePendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Remove((entryId, emojiId));

    // Private methods

    private void ConfirmReflected(ChatEntryId entryId, Reaction? ownReaction)
    {
        // Confirmed here, not on completion, so nothing blinks until the server's invalidation arrives.
        // A lane runs in order: once its latest command shows on the server, the earlier ones are done too.
        var entries = GetEntries(entryId);
        var lastPending = entries.LastOrDefault(x => x.Stage != QueuedCommandStage.Failed);
        if (lastPending is not { Stage: QueuedCommandStage.Completed })
            return;
        if (!ReactionsOverlay.IsReflected(ownReaction, (Reactions_React)lastPending.Command))
            return;

        foreach (var entry in entries) {
            if (entry.Stage == QueuedCommandStage.Completed)
                CommandQueueUI.Confirm(entry.Command);
        }
    }

    private Emoji? GetVisibleOwnEmoji(ChatEntryId entryId)
    {
        // The latest queued intent, or else the last known state; neither waits for anything.
        // A message with no reactions has no computed model at all, which means nothing to take away.
        if (GetLastPending(GetEntries(entryId)) is { } lastPending)
            return lastPending.MustRemove == true ? null : lastPending.Reaction.Emoji;

        var cModel = Computed.GetExisting(() => Get(entryId, default));
        if (cModel is null || !cModel.IsValue(out var model))
            return null;

        return model?.OwnReaction?.Emoji;
    }

    private IReadOnlyList<QueuedCommandEntry> GetEntries(ChatEntryId entryId)
        => CommandQueueUI.GetEntries(entryId.Value)
            .Where(x => x.Command is Reactions_React)
            .ToArray();

    private static Reactions_React? GetLastPending(IReadOnlyList<QueuedCommandEntry> entries)
        => entries.LastOrDefault(x => x.Stage != QueuedCommandStage.Failed)?.Command as Reactions_React;
}
