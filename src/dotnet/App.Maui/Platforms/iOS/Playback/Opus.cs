using ActualLab.Opus.MaciOS;

namespace ActualChat.App.Maui.Playback;

public static class Opus
{
    public static OpusDecoder CreateDecoder()
    {
        var decoder = new OpusDecoder(AudioNodes.VoiceFormat, out var error);
        error.Assert();
        return decoder;
    }

    public static OpusEncoder CreateEncoder()
    {
        var encoder = new OpusEncoder(AudioNodes.VoiceFormat, out var error);
        error.Assert();
        return encoder;
    }
}
