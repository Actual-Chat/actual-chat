namespace ActualChat.UI.Blazor.App.Services;

public static class ReplyTargetResolver
{
    public static readonly TimeSpan UnboundedRecencyWindow = TimeSpan.MaxValue;

    public static ChatId? Resolve(
        IReadOnlyList<ChatId> armedChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        ChatId? focusedChatId,
        Moment now,
        TimeSpan recencyWindow)
    {
        if (armedChatIds.Count == 0)
            return null;

        ChatId? best = null;
        // Moment.EpochStart precedes every real stamp; now - TimeSpan.MaxValue would overflow.
        var bestAt = recencyWindow == UnboundedRecencyWindow ? Moment.EpochStart : now - recencyWindow;
        foreach (var chatId in armedChatIds) {
            if (lastIncomingVoiceAt.TryGetValue(chatId, out var at) && at > bestAt) {
                bestAt = at;
                best = chatId;
            }
        }
        if (best is not null)
            return best;

        if (focusedChatId is { } focused && armedChatIds.Contains(focused))
            return focused;

        return armedChatIds.Count == 1 ? armedChatIds[0] : null;
    }
}
