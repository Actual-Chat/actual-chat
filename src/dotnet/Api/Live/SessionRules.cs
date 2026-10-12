
namespace ActualChat.Live;

[DataContract, MessagePackObject]
public sealed partial record SessionRules
{
    public static readonly SessionRules Default = new();

    [DataMember(Order = 0), Key(0)]
    public VoiceMode? VoiceModeOverride { get; init; }
    [DataMember(Order = 1), Key(1)]
    public bool VideoAllowed { get; init; } = true;

    public bool HasText() => VoiceModeOverride?.HasText() ?? true;
    public bool HasVoice() => VoiceModeOverride?.HasVoice() ?? true;

    public SessionRules WithText(bool hasText)
        // VoiceMode has no "neither": a session without voice keeps its transcript
        => this with {
            VoiceModeOverride = (hasText, HasVoice()) switch {
                (true, true) => VoiceMode.TextAndVoice,
                (false, true) => VoiceMode.JustVoice,
                _ => VoiceMode.JustText,
            },
        };
}
