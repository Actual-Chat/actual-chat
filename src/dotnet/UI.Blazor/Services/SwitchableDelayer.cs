namespace ActualChat.UI.Blazor.Services;

public class SwitchableDelayer
{
    private static readonly Task<bool> _successTask = Task.FromResult(true);

    private readonly object _lock = new ();
    private bool _shouldDelay;
    private TaskCompletionSource<bool>? _delaySource;

    public SwitchableDelayer(bool shouldDelay = false)
        => _shouldDelay = shouldDelay;

    public Task<bool> CanContinueExecution()
    {
        lock (_lock) {
            if (!_shouldDelay)
                return _successTask;

            var oldDelaySource = _delaySource;
            _delaySource = TaskCompletionSourceExt.New<bool>();
            oldDelaySource?.TrySetResult(false);

            return _delaySource.Task;
        }
    }

    public void Enable(bool shouldDelay)
    {
        lock (_lock) {
            if (!shouldDelay) {
                var oldDelaySource = _delaySource;
                _delaySource = null;
                oldDelaySource?.TrySetResult(true);
            }
            _shouldDelay = shouldDelay;
        }
    }
}
