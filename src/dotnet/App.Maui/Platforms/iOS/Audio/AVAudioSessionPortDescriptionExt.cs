using ActualChat.UI.Blazor.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public static class AVAudioSessionPortDescriptionExt
{
    public static AudioOutputKind GetOutputKind(this AVAudioSessionPortDescription port)
    {
        var portType = port.PortType;
        if (portType == AVAudioSession.PortBuiltInReceiver)
            return AudioOutputKind.Phone;
        if (portType == AVAudioSession.PortBuiltInSpeaker)
            return AudioOutputKind.Speaker;
        if (portType == AVAudioSession.PortBluetoothHfp
            || portType == AVAudioSession.PortBluetoothA2DP
            || portType == AVAudioSession.PortBluetoothLE)
            return AudioOutputKind.Bluetooth;
        if (portType == AVAudioSession.PortHeadphones || portType == AVAudioSession.PortHeadsetMic)
            return AudioOutputKind.Headphones;
        if (portType == AVAudioSession.PortCarAudio)
            return AudioOutputKind.Car;

        return AudioOutputKind.Other;
    }
}
