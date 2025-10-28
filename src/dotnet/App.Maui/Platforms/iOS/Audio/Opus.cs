using ActualLab.Opus.MaciOS;

namespace ActualChat.App.Maui.Audio;

public static class Opus
{
    public static OpusDecoder CreateDecoder()
    {
        var decoder = new OpusDecoder(AudioEngine.VoicePlaybackFormat, out var error);
        error.Assert();
        return decoder;
    }

    public static OpusEncoder CreateEncoder()
    {
        var encoder = new OpusEncoder(AudioEngine.VoiceRecordingFormat, out var error);
        error.Assert();
        return encoder;
    }
}
