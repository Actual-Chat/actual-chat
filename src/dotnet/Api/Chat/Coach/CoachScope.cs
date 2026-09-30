using ActualChat.Users;

namespace ActualChat.Chat.Coach;

// Coaching off wins; else chat flag, then place flag, then the user's defaults. An out-of-scope entry is never analysed
public static class CoachScope
{
    public static bool IsInScope(ChatId chatId, ChatUserSettings chat, ChatUserSettings? place, UserCoachSettings user)
    {
        if (!user.IsCoachingEnabled)
            return false;

        if (chat.IsCoachingEnabled is { } chatFlag)
            return chatFlag;
        if (place?.IsCoachingEnabled is { } placeFlag)
            return placeFlag;

        return !(user.SkipPeerChats && chatId.Kind == ChatKind.Peer);
    }
}
