using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class InputNode(AVAudioNode node, AppUIHub hub) : AudioNode(node, _ => {}, hub)
{
    private int _isTapped;
    public new AVAudioInputNode Node => (AVAudioInputNode)base.Node;

    public IDisposable Tap(AVAudioNodeTapBlock handleSamples)
    {
        if (Interlocked.CompareExchange(ref _isTapped, 1, 0) != 0)
            throw StandardError.Constraint("Already installed tap on this input node");

        var hwFormat = GetOutputFormat();
        var frameLength = (int)(hwFormat.SampleRate / 1000 * Constants.Audio.OpusFrameDurationMs);
        return TapInternal(frameLength,
            hwFormat,
            handleSamples,
            void () => Interlocked.Exchange(ref _isTapped, 0));
    }

    public void SetVoiceProcessingEnabled(bool value)
    {
        lock (Lock) {
            Node.SetVoiceProcessingEnabled(value, out var error);
            error.Assert();
        }
    }

    public void Reset()
    {
        lock (Lock) {
            Node.RemoveTapOnBus(Bus);
            Interlocked.Exchange(ref _isTapped, 0);
            Node.Reset();
        }
    }
}
