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
    [DataMember, Key(3)]
    public CarLink Link { get; init; } = CarLink.Call;
}

// Auto is the zero default on both axes and reads as the car's call route: car microphone and
// car speakers, the way a phone call goes.
public enum CarAudioDevice
{
    Auto = 0,
    Phone = 1,
    Car = 2,
}

// What the car is told its hands-free channel carries when the car records. Both are the same
// Bluetooth SCO link; only Call makes the head unit show its phone screen.
public enum CarLink
{
    Call = 0,
    Assistant = 1,
}
