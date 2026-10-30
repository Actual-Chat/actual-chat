using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class InputNode(AVAudioNode node, AppUIHub hub) : AudioNode(node, _ => {}, hub)
{
    private int _isTapped;
    public new AVAudioInputNode Node => (AVAudioInputNode)base.Node;
    // AVAudioInputNode binds the setter but not the getter, so the state is tracked here.
    public bool IsVoiceProcessingEnabled { get; private set; }

    // Unlike mixer and player nodes, nothing else releases the input node: AudioEngine drops its
    // reference without disposing it, so this managed peer outlives the engine as the native
    // node's last owner. Voice processing goes off first, since it can only be toggled while the
    // node still belongs to a live engine. Runs under Lock, from Dispose().
    protected override void DisposeCore()
    {
        if (IsVoiceProcessingEnabled) {
            Node.SetVoiceProcessingEnabled(false, out var error);
            if (error is not null)
                Log.LogWarning("Failed to disable voice processing: {Error}", error.LocalizedDescription);
            IsVoiceProcessingEnabled = false;
        }
        Node.DisposeSilently();
    }

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
            IsVoiceProcessingEnabled = value;
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
