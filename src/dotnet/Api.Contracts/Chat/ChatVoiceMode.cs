using ActualChat.Users;

namespace ActualChat.Chat;

public sealed record ChatVoiceMode(
    ChatId ChatId,
    VoiceMode VoiceMode,
    bool CanChange);
