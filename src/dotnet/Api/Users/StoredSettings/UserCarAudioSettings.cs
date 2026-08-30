using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for audio routing while Android Auto projection is active:
/// which microphone records, and where playback goes.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserCarAudioSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserCarAudioSettings>
{
    public static string KvasKey => nameof(UserCarAudioSettings);

    [DataMember, Key(0)]
    public string Origin { get; init; } = "";
    [DataMember, Key(1)]
    public CarAudioDevice Microphone { get; init; } = CarAudioDevice.Auto;
    [DataMember, Key(2)]
    public CarAudioDevice Output { get; init; } = CarAudioDevice.Auto;
}

// Auto is the zero default on both axes: phone microphone, car speakers.
public enum CarAudioDevice
{
    Auto = 0,
    Phone = 1,
    Car = 2,
}
