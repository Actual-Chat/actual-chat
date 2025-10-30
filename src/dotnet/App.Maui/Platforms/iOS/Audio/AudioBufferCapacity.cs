using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Audio;

internal sealed class AudioBufferCapacity(AppUIHub hub)
{
    private static readonly int LowBufferSize =
        (int)(Constants.Audio.LowPlaybackBufferDuration / Constants.Audio.OpusFrameDuration);
    private static readonly int MaxRenderedBufferCount =
        (int)((Constants.Audio.LowPlaybackBufferDuration + TimeSpan.FromSeconds(10)) / Constants.Audio.OpusFrameDuration);
    private readonly SemaphoreSlim _queuedSemaphore = new (MaxRenderedBufferCount, MaxRenderedBufferCount);
    private readonly MutableState<bool> _isBufferLow = hub.StateFactory.NewMutable(true, StateCategories.Get(typeof(AudioBufferCapacity), nameof(IsBufferLow)));

    public IState<bool> IsBufferLow => _isBufferLow;

    public async Task AcquireAll(CancellationToken cancellationToken)
    {
        for (var i = 0; i < MaxRenderedBufferCount; i++)
            await Acquire(cancellationToken).ConfigureAwait(false);
    }

    public async Task Acquire(CancellationToken cancellationToken)
    {
        await _queuedSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        UpdateState();
    }

    public void Release()
    {
        _queuedSemaphore.ReleaseSilently();
        UpdateState();
    }

    private void UpdateState()
        => _isBufferLow.Value = MaxRenderedBufferCount - _queuedSemaphore.CurrentCount < LowBufferSize;
}
