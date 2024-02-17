namespace ActualChat.Users;

public enum VoiceMode
{
    TextAndVoice,
    JustText,
    JustVoice,
}

public static class VoiceModeExt
{
    public static bool HasVoice(this VoiceMode voiceMode)
        => voiceMode != VoiceMode.JustText;

    public static bool HasText(this VoiceMode voiceMode)
        => voiceMode != VoiceMode.JustVoice;
}
