using ActualChat.Users;

namespace ActualChat.Chat.Coach;

public static class CoachScopeKvas
{
    public static async Task<bool> IsInScope(
        UserScopedKvasBackend kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var user = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        var chat = await kvas.ChatUserSettings(chatId).Get(cancellationToken).ConfigureAwait(false);
        ChatUserSettings? place = null;
        var root = chatId.RootChatId;
        if (root != chatId)
            place = await kvas.ChatUserSettings(root).Get(cancellationToken).ConfigureAwait(false);
        return CoachScope.IsInScope(chatId, chat, place, user);
    }
}
