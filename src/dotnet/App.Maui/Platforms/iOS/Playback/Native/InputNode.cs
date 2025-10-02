using ActualLab.Internal;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class InputNode(AVAudioNode node) : AudioNode(node, _ => {})
{
    private int _isTapped;
    public IDisposable Tap(AVAudioNodeTapBlock handleSamples)
    {
        if (Interlocked.CompareExchange(ref _isTapped, 1, 0) != 0)
            throw Errors.Constraint("Already installed tap on this input node");

        var hwFormat = GetOutputFormat();
        var frameLength = (int)(hwFormat.SampleRate / 1000 * Constants.Audio.OpusFrameDurationMs);
        return TapInternal(frameLength,
            hwFormat,
            handleSamples,
            void () => Interlocked.Exchange(ref _isTapped, 0));
    }
}
