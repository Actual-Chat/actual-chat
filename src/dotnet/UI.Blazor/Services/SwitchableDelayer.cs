namespace ActualChat.UI.Blazor.Services;

public class SwitchableDelayer
{
    private readonly object _lock = new ();
    private bool _shouldDelay;
    private Task<Unit>? _delayTask;

    public Task Use()
    {
        lock (_lock) {
            if (!_shouldDelay)
                return Task.CompletedTask;
            return _delayTask ??= TaskSource.New<Unit>(false).Task;
        }
    }

    public void Enable(bool shouldDelay)
    {
        lock (_lock) {
            if (!shouldDelay) {
                if (_delayTask != null) {
                    TaskSource.For(_delayTask).TrySetResult(Unit.Default);
                    _delayTask = null;
                }
            }
            _shouldDelay = shouldDelay;
        }
    }

    public SwitchableDelayer(bool shouldDelay = false)
        => _shouldDelay = shouldDelay;
}
