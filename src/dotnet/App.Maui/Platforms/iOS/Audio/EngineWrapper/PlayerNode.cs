using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class PlayerNode : AudioNode, IDisposable
{
    public AVAudioFormat Format { get; }
    private readonly Lock _lock = new();
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
        _isPlaying.DisposeSilently();
        Stop();
    }

    public void Play()
    {
        lock (_lock)
            if (!Node.Playing)
                Node.Play();
        _isPlaying.Invalidate();
    }

    public void Pause()
    {
        lock (_lock)
            if (Node.Playing)
                Node.Pause();
        _isPlaying.Invalidate();
    }

    public void ScheduleBuffer(AVAudioPcmBuffer pcm, Action<AVAudioPlayerNodeCompletionCallbackType> callback)
    {
        lock (_lock)
            Node.ScheduleBuffer(pcm, AVAudioPlayerNodeCompletionCallbackType.PlayedBack, callback);
    }

    public Task ScheduleFileAndWait(AVAudioFile audioFile, CancellationToken cancellationToken = default)
    {
        var whenPlayed = AsyncTaskMethodBuilderExt.New();
        lock (_lock)
            Node.ScheduleFile(audioFile,
                null,
                AVAudioPlayerNodeCompletionCallbackType.PlayedBack,
                _ => whenPlayed.TrySetResult());
        return whenPlayed.Task.WaitAsync(cancellationToken);
    }

    public void Stop()
    {
        lock (_lock)
            Node.Stop();
        _isPlaying.Invalidate();
    }

    private Task<bool> GetIsPlaying(CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(Node.Playing);
    }
}
