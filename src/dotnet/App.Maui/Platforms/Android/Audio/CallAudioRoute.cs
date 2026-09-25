namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Where a call's audio goes: a connected headset unless <see cref="IsBuiltinForced"/>,
/// otherwise the earpiece or the loudspeaker. The default is the headset, then the loudspeaker.
/// </summary>
public readonly record struct CallAudioRoute(bool IsEarpiece, bool IsBuiltinForced);
