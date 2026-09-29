using ActualChat.Users;

namespace ActualChat.Chat.Coach;

// Chat flag, then place flag, then the user's defaults; an out-of-scope entry is never analysed
public static class CoachScope
{
    public static bool IsInScope(ChatId chatId, ChatUserSettings chat, ChatUserSettings? place, UserCoachSettings user)
    {
        if (chat.IsCoachingEnabled is { } chatFlag)
            return chatFlag;
        if (place?.IsCoachingEnabled is { } placeFlag)
            return placeFlag;

        return !(user.SkipPeerChats && chatId.Kind == ChatKind.Peer);
    }

    public static async Task<bool> IsInScope(
        UserScopedKvasBackend kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var user = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        var chat = await kvas.ChatUserSettings(chatId).Get(cancellationToken).ConfigureAwait(false);
        ChatUserSettings? place = null;
        var root = chatId.RootChatId;
        if (root != chatId)
            place = await kvas.ChatUserSettings(root).Get(cancellationToken).ConfigureAwait(false);
        return IsInScope(chatId, chat, place, user);
    }
}
