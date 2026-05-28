using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class ResamplerFactory(AppUIHub hub)
{
    public Resampler Create(AVAudioFormat sourceFormat, AVAudioFormat targetFormat)
        => new(sourceFormat, targetFormat, hub.LogFor<Resampler>());
}
