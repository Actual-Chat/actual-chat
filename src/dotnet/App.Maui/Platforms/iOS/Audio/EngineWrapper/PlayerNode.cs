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
        Stop();
        _isPlaying.DisposeSilently();
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

    public async Task ScheduleFileAndWait(AVAudioFile audioFile, CancellationToken cancellationToken = default)
    {
        using var _ = Disposable.New(Node.Stop);
        await Node.ScheduleFileAsync(audioFile, null, AVAudioPlayerNodeCompletionCallbackType.PlayedBack)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
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
