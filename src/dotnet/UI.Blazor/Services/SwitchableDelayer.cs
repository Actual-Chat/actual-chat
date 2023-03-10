namespace ActualChat.UI.Blazor.Services;

public class SwitchableDelayer
{
    private readonly object _syncObject = new ();
    private bool _shouldDelay;
    private Task<Unit>? _delayTask;

    public Task Use()
    {
        lock (_syncObject) {
            if (!_shouldDelay)
                return Task.CompletedTask;
            return _delayTask ??= TaskSource.New<Unit>(false).Task;
        }
    }

    public void Enable(bool shouldDelay)
    {
        lock (_syncObject) {
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
