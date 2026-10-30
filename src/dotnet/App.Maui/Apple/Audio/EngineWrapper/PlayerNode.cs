using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class PlayerNode : AudioNode, IDisposable
{
    public AVAudioFormat Format { get; }
    private readonly ComputedState<bool> _isPlaying;
    private bool _isPlayRequested;

    public IState<bool> IsPlaying => _isPlaying;
    public bool IsPlayRequested {
        get {
            lock (Lock)
                return _isPlayRequested;
        }
    }
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
        lock (Lock) {
            _isPlayRequested = true;
            if (!Node.Playing)
                Node.Play();
        }
        _isPlaying.Invalidate();
    }

    public void Pause()
    {
        lock (Lock) {
            _isPlayRequested = false;
            if (Node.Playing)
                Node.Pause();
        }
        _isPlaying.Invalidate();
    }

    public bool RestorePlayState()
    {
        // AVAudioEngine.Stop() stops its player nodes, and on a configuration change the engine
        // stops itself, so the intent to play has to be restated once it's running again. Playing
        // can keep reporting true across that, so it only decides what to report, not whether to
        // act - Play() on a node that really is live is a no-op.
        bool wasPlaying;
        lock (Lock) {
            if (!_isPlayRequested)
                return false;

            wasPlaying = Node.Playing;
            Node.Play();
        }
        _isPlaying.Invalidate();
        return !wasPlaying;
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
        lock (Lock) {
            _isPlayRequested = false;
            Node.Stop();
        }
        _isPlaying.Invalidate();
    }

    private Task<bool> GetIsPlaying(CancellationToken cancellationToken)
    {
        lock (Lock)
            return Task.FromResult(Node.Playing);
    }
}
