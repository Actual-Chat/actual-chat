using ActualChat.Users;

namespace ActualChat.Chat;

[StructLayout(LayoutKind.Auto)]
public record struct ChatVoiceMode(
    ChatId ChatId,
    VoiceMode VoiceMode,
    bool CanChange);
