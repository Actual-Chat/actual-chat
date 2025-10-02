using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public abstract class AudioNode(AVAudioNode node, Action<AVAudioNode> disposer)
{
    public const int Bus = 0;
    protected readonly Lock Lock = new();
    protected internal AVAudioNode Node => node;

    public void Dispose()
    {
        lock (Lock) {
            DisposeCore();
            disposer(node);
        }
    }

    protected virtual void DisposeCore()
    { }

    public AVAudioFormat GetOutputFormat()
    {
        lock (Lock)
            return node.GetBusOutputFormat(Bus);
    }

    public IDisposable Tap(int desiredBufferSize, AVAudioFormat format, AVAudioNodeTapBlock callback)
        => TapInternal(desiredBufferSize, format, callback, null);

    protected IDisposable TapInternal(int desiredBufferSize, AVAudioFormat format, AVAudioNodeTapBlock callback, Action? onDispose)
    {
        lock (Lock)
            node.InstallTapOnBus(Bus, (uint)desiredBufferSize, format, callback);

        return Disposable.New(() => {
            lock (Lock)
                node.RemoveTapOnBus(Bus);
            onDispose?.Invoke();
        });
    }
}
