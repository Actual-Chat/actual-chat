using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class PlayerNode : AudioNode, IDisposable
{
    public AVAudioFormat Format { get; }
    private readonly ComputedState<bool> _isPlaying;

    public IState<bool> IsPlaying => _isPlaying;
    public new AVAudioPlayerNode Node => (AVAudioPlayerNode)base.Node;

    public PlayerNode(AVAudioPlayerNode node, AVAudioFormat format, Action<AVAudioNode> disposer, AppUIHub hub) : base(node, disposer, hub)
    {
        Format = format;

        _isPlaying = hub.StateFactory.NewComputed(GetIsPlaying, StateCategories.Get(GetType(), nameof(IsPlaying)));
    }

    protected override void DisposeCore()
    {
        Stop();
        _isPlaying.DisposeSilently();
    }

    public void Play()
    {
        lock (Lock)
            if (!Node.Playing)
                Node.Play();
        _isPlaying.Invalidate();
    }

    public void Pause()
    {
        lock (Lock)
            if (Node.Playing)
                Node.Pause();
        _isPlaying.Invalidate();
    }

    public void ScheduleBuffer(AVAudioPcmBuffer pcm, Action<AVAudioPlayerNodeCompletionCallbackType> callback)
    {
        lock (Lock)
            Node.ScheduleBuffer(pcm, AVAudioPlayerNodeCompletionCallbackType.PlayedBack, callback);
    }

    public Task ScheduleFileAndWait(AVAudioFile audioFile, CancellationToken cancellationToken = default)
    {
        var whenPlayed = AsyncTaskMethodBuilderExt.New();
        lock (Lock)
            // NOTE: ScheduleFileAsync seems to have a synchronous continuation and leads to deadlock
 #pragma warning disable CA1849
            Node.ScheduleFile(audioFile,
                null,
                AVAudioPlayerNodeCompletionCallbackType.PlayedBack,
                _ => whenPlayed.TrySetResult());
 #pragma warning restore CA1849
        return whenPlayed.Task.WaitAsync(cancellationToken);
    }

    public void Stop()
    {
        lock (Lock)
            Node.Stop();
        _isPlaying.Invalidate();
    }

    private Task<bool> GetIsPlaying(CancellationToken cancellationToken)
    {
        lock (Lock)
            return Task.FromResult(Node.Playing);
    }
}
