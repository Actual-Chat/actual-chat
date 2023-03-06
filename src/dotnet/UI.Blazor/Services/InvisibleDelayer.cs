namespace ActualChat.UI.Blazor.Services;

public class InvisibleDelayer
{
    private readonly object _syncObject = new ();
    private bool _isVisible;
    private Task<Unit>? _delayTask;

    public Task Use()
    {
        lock (_syncObject) {
            if (_isVisible) {
                return Task.CompletedTask;
            }
            else {
                return _delayTask ??= TaskSource.New<Unit>(false).Task;
            }
        }
    }

    public void SetIsVisible(bool isVisible)
    {
        lock (_syncObject) {
            if (isVisible) {
                if (_delayTask != null) {
                    // complete existing delay task
                    TaskSource.For(_delayTask).TrySetResult(Unit.Default);
                    _delayTask = null;
                }
            }
            else {
                // do nothing
            }
            this._isVisible = isVisible;
        }
    }

    public InvisibleDelayer(bool isVisible = true)
    {
        this._isVisible = isVisible;
    }
}
