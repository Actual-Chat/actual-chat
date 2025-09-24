using ActualLab.Opus.MaciOS;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public static class Opus
{
    private static readonly AVAudioFormat PcmFormat = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, 48000, 1, false);

    public static OpusDecoder CreateDecoder()
    {
        var decoder = new OpusDecoder(PcmFormat, out var error);
        error.Assert();
        return decoder;
    }

    public static OpusEncoder CreateEncoder()
    {
        var encoder = new OpusEncoder(PcmFormat, out var error);
        error.Assert();
        return encoder;
    }
}
