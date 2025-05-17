using ActualChat.Users;

namespace ActualChat.Chat;

[StructLayout(LayoutKind.Auto)]
public sealed record ChatVoiceMode(
    ChatId ChatId,
    VoiceMode VoiceMode,
    bool CanChange);
