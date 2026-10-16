using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class PlayerNode : AudioNode, IDisposable
{
    public AVAudioFormat Format { get; }
    private readonly ComputedState<bool> _isPlaying;
    // Guards the two members below alone and is never held across a call into the node: the
    // completion callbacks take it on AVFoundation's own thread.
    private readonly Lock _holdLock = new();
    private readonly List<HeldBuffer> _heldBuffers = new();
    private bool _isOnHold;
    private long _nextBufferIndex;
    private bool _isPlayRequested;

    public IState<bool> IsPlaying => _isPlaying;
    public bool IsPlayRequested {
        get {
            lock (Lock)
                return _isPlayRequested;
        }
    }
    public new AVAudioPlayerNode Node => (AVAudioPlayerNode)base.Node;
    private bool IsOnHold {
        get {
            lock (_holdLock)
                return _isOnHold;
        }
    }

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
            if (!IsOnHold && !Node.Playing)
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
            if (!_isPlayRequested || IsOnHold)
                return false;

            wasPlaying = Node.Playing;
            Node.Play();
        }
        _isPlaying.Invalidate();
        return !wasPlaying;
    }

    public void ScheduleBuffer(AVAudioPcmBuffer pcm, Action<AVAudioPlayerNodeCompletionCallbackType> callback)
    {
        lock (Lock) {
            var buffer = new HeldBuffer(_nextBufferIndex++, pcm, callback);
            // Kept, not queued, while on hold: the node gives back what it has only when the
            // engine stops, and a buffer queued after that would play ahead of all of those.
            lock (_holdLock) {
                if (_isOnHold) {
                    _heldBuffers.Add(buffer);
                    return;
                }
            }
            ScheduleBufferUnsafe(buffer);
        }
    }

    public void Hold()
    {
        // An engine that stops - which it does on its own once a session switch lands - drops
        // every buffer its player nodes have queued and reports each as played: up to ten
        // seconds of a replay gone, and the track ended early with them. A node on hold is
        // silent and keeps what comes back that way, for Unhold to queue again.
        lock (_holdLock)
            _isOnHold = true;
        lock (Lock) {
            if (Node.Playing)
                Node.Pause();
        }
        _isPlaying.Invalidate();
    }

    public void Unhold()
    {
        lock (Lock) {
            HeldBuffer[] heldBuffers;
            lock (_holdLock) {
                if (!_isOnHold)
                    return;

                _isOnHold = false;
                heldBuffers = [.. _heldBuffers];
                _heldBuffers.Clear();
            }
            // The stop's callbacks and the buffers fed meanwhile arrive interleaved.
            foreach (var buffer in heldBuffers.OrderBy(x => x.Index))
                ScheduleBufferUnsafe(buffer);
            if (_isPlayRequested)
                Node.Play();
        }
        _isPlaying.Invalidate();
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
        HeldBuffer[] heldBuffers;
        lock (Lock) {
            _isPlayRequested = false;
            // Off hold before the stop, so the buffers it flushes are reported, not kept.
            lock (_holdLock) {
                _isOnHold = false;
                heldBuffers = [.. _heldBuffers];
                _heldBuffers.Clear();
            }
            Node.Stop();
        }
        // Whoever scheduled them waits for these: a stop reports its buffers, queued or held.
        foreach (var buffer in heldBuffers)
            buffer.Callback(AVAudioPlayerNodeCompletionCallbackType.PlayedBack);
        _isPlaying.Invalidate();
    }

    // Private methods

    private void ScheduleBufferUnsafe(HeldBuffer buffer)
        => Node.ScheduleBuffer(buffer.Pcm, AVAudioPlayerNodeCompletionCallbackType.PlayedBack, type => {
            lock (_holdLock) {
                if (_isOnHold) {
                    _heldBuffers.Add(buffer);
                    return;
                }
            }
            buffer.Callback(type);
        });

    private Task<bool> GetIsPlaying(CancellationToken cancellationToken)
    {
        lock (Lock)
            return Task.FromResult(Node.Playing);
    }

    // Nested types

    private readonly record struct HeldBuffer(
        long Index,
        AVAudioPcmBuffer Pcm,
        Action<AVAudioPlayerNodeCompletionCallbackType> Callback);
}
