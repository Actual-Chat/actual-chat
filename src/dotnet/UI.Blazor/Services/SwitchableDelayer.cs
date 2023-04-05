namespace ActualChat.UI.Blazor.Services;

public class SwitchableDelayer
{
    private static readonly Task<bool> _successTask = Task.FromResult(true);

    private readonly object _lock = new ();
    private bool _shouldDelay;
    private Task<bool>? _delayTask;

    public SwitchableDelayer(bool shouldDelay = false)
        => _shouldDelay = shouldDelay;

    public Task<bool> CanContinueExecution()
    {
        lock (_lock) {
            if (!_shouldDelay)
                return _successTask;

            // let's abort if there is a awaiting call to wait in the call with the latest arguments
            var delayTask = _delayTask;
            if (delayTask != null)
                TaskSource.For(delayTask).TrySetResult(false);
            return _delayTask = TaskSource.New<bool>(false).Task;
        }
    }

    public void Enable(bool shouldDelay)
    {
        lock (_lock) {
            if (!shouldDelay) {
                var delayTask = _delayTask;
                if (delayTask != null) {
                    TaskSource.For(delayTask).TrySetResult(true);
                    _delayTask = null;
                }
            }
            _shouldDelay = shouldDelay;
        }
    }
}
