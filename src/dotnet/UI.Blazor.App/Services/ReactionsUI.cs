using ActualChat.Chat;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Reads reactions with the not-yet-confirmed queued ones folded in,
/// so a reaction shows up the moment it's queued rather than when the server answers.
/// </summary>
public class ReactionsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly object Seen = new();

    private readonly HashSet<(string EntryId, string EmojiId)> _pendingAnimations = new();
    // Weak, so a command leaves it along with its queue entry
    private readonly ConditionalWeakTable<ICommand, object> _seenCommands = new();
    private IReactions Reactions => Hub.Reactions;
    private CommandQueueUI CommandQueueUI => Hub.CommandQueueUI;

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

    [ComputeMethod]
    public virtual async Task<ReactionsModel?> Get(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        await CommandQueueUI.OnChanged(entryId.Value).ConfigureAwait(false);
        var summaries = await Reactions.ListSummaries(Session, entryId, cancellationToken).ConfigureAwait(false);
        Reaction? ownReaction = null;
        if (summaries.Length > 0)
            ownReaction = await Reactions.Get(Session, entryId, cancellationToken).ConfigureAwait(false);

        var entries = GetEntries(entryId);
        var lastPending = GetLastPending(entries);
        // A completed command is folded in until the server shows it - dropping it on completion would blink
        // the badge back to the old state until the server's invalidation arrives - and never after:
        // a later difference is a newer change, e.g. made on another device.
        // A lane runs in order, so the server showing the latest command means the earlier ones are done too.
        if (lastPending is { Stage: CommandStage.Completed }
            && ReactionsOverlay.IsReflected(ownReaction, (Reactions_React)lastPending.Command)) {
            _seenCommands.TryAdd(lastPending.Command, Seen);
            lastPending = null;
        }
        var lastStage = entries.Count > 0 && entries[^1].Stage == CommandStage.Failed
            ? CommandStage.Failed
            : lastPending?.Stage;
        if (lastPending is null) {
            // Only failed commands are left, if any: the server state is the truth,
            // and the row is marked with the reaction that didn't get through
            return summaries.Length == 0 ? null : new ReactionsModel(summaries, ownReaction, lastStage);
        }

        var model = ReactionsOverlay.Fold(summaries, ownReaction, (Reactions_React)lastPending.Command);

        // A queued removal of the last reaction leaves nothing to show, and the row renders
        // its add button even with no badges in it - an empty strip under the message
        return model.IsEmpty ? null : model with { PendingStage = lastStage };
    }

    public void React(ChatEntryId entryId, Emoji emoji)
    {
        _ = TuneUI.Play(Tune.React);
        var mustRemove = GetVisibleOwnEmoji(entryId) == emoji;
        if (mustRemove) {
            // Taken back before its badge rendered: it mustn't play once the reaction shows up some other way
            _pendingAnimations.Remove((entryId.Value, emoji.Id.Value));
        }
        else {
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
        _ = UICommander.Run(reactCmd);
    }

    public bool RemovePendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Remove((entryId, emojiId));

    // Private methods

    private Emoji? GetVisibleOwnEmoji(ChatEntryId entryId)
    {
        // The latest queued intent, or else the last known state; neither waits for anything.
        // A message with no reactions has no computed model at all, which means nothing to take away.
        if (GetLastPending(GetEntries(entryId))?.Command is Reactions_React lastPending)
            return lastPending.MustRemove == true ? null : lastPending.Reaction.Emoji;

        var cModel = Computed.GetExisting(() => Get(entryId, default));
        if (cModel is null || !cModel.IsValue(out var model))
            return null;

        return model?.OwnReaction?.Emoji;
    }

    private IReadOnlyList<CommandEntry> GetEntries(ChatEntryId entryId)
        => CommandQueueUI.GetEntries(entryId.Value)
            .Where(x => x.Command is Reactions_React)
            .ToArray();

    private CommandEntry? GetLastPending(IReadOnlyList<CommandEntry> entries)
        // A command the server has shown is done with, even while its entry is still around
        => entries.LastOrDefault(x => x.Stage != CommandStage.Failed) is { } lastPending
            && !_seenCommands.TryGetValue(lastPending.Command, out _)
            ? lastPending
            : null;
}
