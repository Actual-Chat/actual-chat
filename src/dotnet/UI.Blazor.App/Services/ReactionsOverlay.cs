using ActualChat.Chat;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record ReactionsModel(
    ReactionSummary[] Summaries,
    Reaction? OwnReaction,
    QueuedCommandStage? PendingStage = null);

/// <summary>
/// Predicts what the server will store for a chat entry once its queued
/// <see cref="Reactions_React"/> commands are applied.
/// </summary>
public static class ReactionsOverlay
{
    public static ReactionsModel Fold(
        ReactionSummary[] summaries,
        Reaction? ownReaction,
        IReadOnlyList<Emoji> pendingEmojis,
        ChatEntryId entryId)
    {
        if (pendingEmojis.Count == 0)
            return new ReactionsModel(summaries, ownReaction);

        // Mirrors ReactionsBackend.OnReact: the same emoji removes the reaction,
        // a different one replaces it, and none adds it.
        var newSummaries = summaries.ToList();
        var own = ownReaction;
        foreach (var emoji in pendingEmojis) {
            if (own is { } ownNow && ownNow.Emoji == emoji) {
                UpdateCount(newSummaries, entryId, emoji, -1);
                own = null;
                continue;
            }

            if (own is { } previous)
                UpdateCount(newSummaries, entryId, previous.Emoji, -1);
            UpdateCount(newSummaries, entryId, emoji, +1);
            own = new Reaction {
                Id = Symbol.Empty,
                AuthorId = default!,
                EntryId = entryId,
                Emoji = emoji,
            };
        }
        return new ReactionsModel(newSummaries.ToArray(), own);
    }

    public static bool IsReflected(Reaction? ownReaction, Emoji pendingEmoji)
        => ownReaction?.Emoji == pendingEmoji;

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
