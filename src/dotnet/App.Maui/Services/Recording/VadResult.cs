namespace ActualChat.App.Maui.Services.Recording;

public readonly record struct VadResult(VoiceActivityChange? Change, double Gain)
{
    [MemberNotNullWhen(true, nameof(Change))]
    public bool HasEvent => Change.HasValue;
    public static VadResult Event(VoiceActivityChange change) => new (change, double.NaN);
    public static VadResult GainOnly(double gain) => new (null, gain);
}
