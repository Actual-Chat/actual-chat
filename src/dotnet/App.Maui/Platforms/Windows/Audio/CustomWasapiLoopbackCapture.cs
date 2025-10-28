using NAudio.CoreAudioApi;

namespace ActualChat.App.Maui.Audio;

public class CustomWasapiLoopbackCapture(MMDevice captureDevice, bool useEventSync, int audioBufferMillisecondsLength)
    : WasapiCapture(captureDevice, useEventSync, audioBufferMillisecondsLength)
{
    protected override AudioClientStreamFlags GetAudioClientStreamFlags()
        => AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
}
