using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Audio;

internal sealed class AudioBufferCapacity(AppUIHub hub)
{
    private static readonly int LowBufferSize =
        (int)(Constants.Audio.LowPlaybackBufferDuration / Constants.Audio.OpusFrameDuration);
    // An engine that stops by itself, as it does when the output changes, flushes its player nodes
    // and reports every queued buffer as played. So what's scheduled is what such a change drops,
    // and the rest of the backlog waits in the decoder's output instead.
    private static readonly int MaxScheduledBufferCount =
        (int)(TimeSpan.FromSeconds(1) / Constants.Audio.OpusFrameDuration);
    private readonly SemaphoreSlim _scheduledSemaphore = new (MaxScheduledBufferCount, MaxScheduledBufferCount);
    private readonly MutableState<bool> _isBufferLow = hub.StateFactory.NewMutable(true, StateCategories.Get(typeof(AudioBufferCapacity), nameof(IsBufferLow)));
    private int _bufferedCount;

    public IState<bool> IsBufferLow => _isBufferLow;

    public void AddBuffered()
    {
        Interlocked.Increment(ref _bufferedCount);
        UpdateState();
    }

    public async Task AcquireAll(CancellationToken cancellationToken)
    {
        for (var i = 0; i < MaxScheduledBufferCount; i++)
            await Acquire(cancellationToken).ConfigureAwait(false);
    }

    public Task Acquire(CancellationToken cancellationToken)
        => _scheduledSemaphore.WaitAsync(cancellationToken);

    public void Release()
    {
        _scheduledSemaphore.ReleaseSilently();
        Interlocked.Decrement(ref _bufferedCount);
        UpdateState();
    }

    private void UpdateState()
        => _isBufferLow.Value = Volatile.Read(ref _bufferedCount) < LowBufferSize;
}
