using ActualChat.Kvas;

namespace ActualChat.Chat;

/// <summary>
/// Chat-scoped list of pinned message ids (Telegram-style), stored via chat-scoped KVAS.
/// Order in <see cref="EntryIds"/> is the display order; capped at <see cref="MaxCount"/>.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatPinnedEntries : IHasKvasKey<ChatPinnedEntries>
{
    public const int MaxCount = 3;
    public static readonly ChatPinnedEntries None = new();
    public static string KvasKey => "@PinnedEntries";

    [DataMember, Key(0)] public ApiArray<ChatEntryId> EntryIds { get; init; }

    public static async Task<ApiArray<ChatEntryId>> Apply(
        ApiArray<ChatEntryId> entryIds,
        ChatEntryId entryId,
        bool mustPin,
        int maxCount,
        Func<ChatEntryId, CancellationToken, Task<bool>> isPresent,
        CancellationToken cancellationToken)
    {
        // Shared by the chat's and the personal pin lists. The stored list is re-normalized on every change:
        // ids of messages removed since the last write are dropped, so they never linger against the cap.
        var chatId = entryId.ChatId;
        var result = new List<ChatEntryId>(maxCount);
        foreach (var id in entryIds) {
            if (id.LocalId <= 0 || id.ChatId != chatId || id == entryId || result.Contains(id))
                continue;

            var isIdPresent = await isPresent(id, cancellationToken).ConfigureAwait(false);
            if (isIdPresent)
                result.Add(id);
        }

        if (mustPin) {
            var isEntryPresent = await isPresent(entryId, cancellationToken).ConfigureAwait(false);
            if (!isEntryPresent)
                throw StandardError.Constraint("This message no longer exists.");
            if (result.Count >= maxCount)
                throw StandardError.Constraint($"You can pin up to {maxCount} messages.");

            result.Add(entryId);
        }

        // Order is always by message chronology (Telegram-style); pins can't be reordered manually.
        result.Sort((a, b) => a.LocalId.CompareTo(b.LocalId));
        return result.ToApiArray();
    }
}
