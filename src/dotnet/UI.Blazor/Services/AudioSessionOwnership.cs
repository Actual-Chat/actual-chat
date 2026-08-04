namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Who currently owns AVAudioSession activation. Apple-only in practice, but the transition
/// rules live here — outside the platform projects — so they can be tested.
/// </summary>
public enum AudioSessionOwner
{
    App = 0,
    PttPlayback,
    PttTransmit,
}

public enum AudioSessionRelease
{
    Deactivated = 0,
    TransmitEnded,
    ChannelLeft,
}

public static class AudioSessionOwnership
{
    public static AudioSessionOwner OnActivated(bool isTransmitting)
        => isTransmitting ? AudioSessionOwner.PttTransmit : AudioSessionOwner.PttPlayback;

    public static AudioSessionOwner OnReleased(AudioSessionOwner current, AudioSessionRelease release)
        => release switch {
            AudioSessionRelease.Deactivated => AudioSessionOwner.App,
            AudioSessionRelease.ChannelLeft => AudioSessionOwner.App,
            // Full duplex: ending a transmit must not steal the session from a running playback.
            AudioSessionRelease.TransmitEnded when current == AudioSessionOwner.PttTransmit
                => AudioSessionOwner.App,
            _ => current,
        };

    public static bool MayActivate(AudioSessionOwner owner)
        => owner == AudioSessionOwner.App;

    public static bool MayConfigure(AudioSessionOwner owner)
        => owner != AudioSessionOwner.PttTransmit;
}
