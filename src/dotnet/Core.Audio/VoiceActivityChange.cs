namespace ActualChat.Audio;

[StructLayout(LayoutKind.Auto)]
public readonly record struct VoiceActivityChange(
    VoiceActivityKind Kind,
    int OffsetSamples,
    int? DurationSamples,
    double SpeechProb)
{
    public static VoiceActivityChange NoVoiceActivity => new (VoiceActivityKind.End, 0, null, 0);

    public double OffsetSeconds => OffsetSamples / (double)VoiceActivityDetector.SampleRate;
    public double? DurationSeconds => DurationSamples / (double)VoiceActivityDetector.SampleRate;

    public static VoiceActivityChange Start(int offsetSamples, int? durationSamples, double speechProb)
        => new (VoiceActivityKind.Start, offsetSamples, durationSamples, speechProb);

    public static VoiceActivityChange End(int offsetSamples, int? durationSamples, double speechProb)
        => new (VoiceActivityKind.End, offsetSamples, durationSamples, speechProb);
}
