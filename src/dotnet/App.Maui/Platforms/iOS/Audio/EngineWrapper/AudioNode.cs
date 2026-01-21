using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public abstract class AudioNode(AVAudioNode node, Action<AVAudioNode> disposer, AppUIHub hub)
{
    public const int Bus = 0;
    protected readonly Lock Lock = new();
    protected internal AVAudioNode Node => node;
    protected AppUIHub Hub => hub;

    protected ILogger Log => field ??= hub.LogFor(GetType());

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
