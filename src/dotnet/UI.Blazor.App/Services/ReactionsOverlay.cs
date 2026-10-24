using ActualChat.Chat;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record ReactionsModel(
    ReactionSummary[] Summaries,
    Reaction? OwnReaction,
    QueuedCommandStage? PendingStage = null)
{
    public bool IsEmpty => Summaries.Length == 0;
}

/// <summary>
/// Predicts what the server will store for a chat entry once its queued
/// <see cref="Reactions_React"/> commands are applied.
/// </summary>
public static class ReactionsOverlay
{
    public static ReactionsModel Fold(
        ReactionSummary[] summaries,
        Reaction? ownReaction,
        Reactions_React lastPending)
    {
        // Every queued command states the outcome it wants, so the last one is where the own reaction
        // ends up, whatever came before it; only the summaries need the difference with the server
        var entryId = lastPending.Reaction.EntryId;
        var emoji = lastPending.Reaction.Emoji;
        var newOwn = lastPending.MustRemove == true
            ? null
            : new Reaction { Id = Symbol.Empty, AuthorId = default!, EntryId = entryId, Emoji = emoji };
        if (ownReaction?.Emoji == newOwn?.Emoji)
            return new ReactionsModel(summaries, ownReaction);

        var newSummaries = summaries.ToList();
        if (ownReaction is not null)
            UpdateCount(newSummaries, entryId, ownReaction.Emoji, -1);
        if (newOwn is not null)
            UpdateCount(newSummaries, entryId, newOwn.Emoji, +1);
        return new ReactionsModel(newSummaries.ToArray(), newOwn);
    }

    public static bool IsReflected(Reaction? ownReaction, Reactions_React command)
        => command.MustRemove == true
            ? ownReaction?.Emoji != command.Reaction.Emoji
            : ownReaction?.Emoji == command.Reaction.Emoji;

    // Private methods

    private static void UpdateCount(List<ReactionSummary> summaries, ChatEntryId entryId, Emoji emoji, int delta)
    {
        for (var i = 0; i < summaries.Count; i++) {
            if (summaries[i].Emoji != emoji)
                continue;

            var newCount = summaries[i].Count + delta;
            if (newCount <= 0)
                summaries.RemoveAt(i);
            else
                summaries[i] = summaries[i] with { Count = newCount };
            return;
        }

        if (delta <= 0)
            return;

        summaries.Add(new ReactionSummary {
            Id = Symbol.Empty,
            EntryId = entryId,
            Emoji = emoji,
            Count = delta,
        });
    }
}
