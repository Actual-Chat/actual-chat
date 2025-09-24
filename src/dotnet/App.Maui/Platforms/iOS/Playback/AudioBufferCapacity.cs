namespace ActualChat.App.Maui.Playback;

internal sealed class AudioBufferCapacity
{
    private static readonly int LowBufferSize =
        (int)(Constants.Audio.LowPlaybackBufferDuration / Constants.Audio.OpusFrameDuration);
    private static readonly int MaxRenderedBufferCount =
        (int)((Constants.Audio.LowPlaybackBufferDuration + TimeSpan.FromSeconds(10)) / Constants.Audio.OpusFrameDuration);
    private readonly SemaphoreSlim _queuedSemaphore = new (MaxRenderedBufferCount, MaxRenderedBufferCount);

    public bool IsBufferLow => MaxRenderedBufferCount - _queuedSemaphore.CurrentCount < LowBufferSize;

    public async Task Acquire(CancellationToken cancellationToken)
        => await _queuedSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

    public async Task AcquireAll(CancellationToken cancellationToken)
    {
        for (var i = 0; i < MaxRenderedBufferCount; i++)
            await _queuedSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Release()
        => _queuedSemaphore.ReleaseSilently();
}
