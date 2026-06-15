using ActualChat.Users;

namespace ActualChat.Live;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record SessionRules
{
    public static readonly SessionRules Default = new();

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public VoiceMode? VoiceModeOverride { get; init; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public bool ScreenShareAllowed { get; init; } = true;
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public bool RecordingAllowed { get; init; } = true;

    public VoiceMode Merge(VoiceMode userMode)
    {
        if (VoiceModeOverride is not { } o)
            return userMode;
        var transcript = userMode != VoiceMode.JustVoice && o != VoiceMode.JustVoice;
        var voice = userMode.HasVoice() && o.HasVoice();
        return (transcript, voice) switch {
            (true, true) => VoiceMode.TextAndVoice,
            (true, false) => VoiceMode.JustText,
            (false, true) => VoiceMode.JustVoice,
            _ => VoiceMode.JustText,
        };
    }
}
