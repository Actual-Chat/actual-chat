using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class ResamplerFactory
{
    public Resampler Create(AVAudioFormat sourceFormat, AVAudioFormat targetFormat)
        => new(sourceFormat, targetFormat);
}
