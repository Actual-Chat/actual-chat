namespace ActualChat.Users;

/// <summary>
/// Specifies whether messages include voice, text, or both.
/// </summary>
public enum VoiceMode
{
    TextAndVoice,
    JustText,
    JustVoice,
}

/// <summary>
/// Extension methods for <see cref="VoiceMode"/>.
/// </summary>
public static class VoiceModeExt
{
    public static bool HasVoice(this VoiceMode voiceMode)
        => voiceMode != VoiceMode.JustText;

    public static bool HasText(this VoiceMode voiceMode)
        => voiceMode != VoiceMode.JustVoice;
}
