using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record UserAndroidSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserAndroidSettings>
{
    public static string KvasKey => nameof(UserAndroidSettings);

    [DataMember, Key(0)]
    public string Origin { get; init; } = "";
    [DataMember, Key(1)]
    public RecordingStartMode RecordingStart { get; init; } = RecordingStartMode.Auto;
}

// Android holds the microphone back until the call audio route settles, which takes about half a
// second. Not waiting starts faster, but a Bluetooth headset may be picked up only after the start.
public enum RecordingStartMode
{
    Auto = 0,
    WaitForRoute = 1,
    DontWait = 2,
}
