using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

public class AutoNavigationTasks(HostKind hostKind)
{
    private readonly object _lock = new();
    private Task _whenCompleted = Task.CompletedTask;
    private bool _isCompleted = hostKind.IsServer();

    public bool Add(Task task)
    {
        if (_isCompleted)
            return false;
        lock (_lock) {
            if (_isCompleted)
                return false;

            _whenCompleted = _whenCompleted.IsCompleted
                ? task
                : Task.WhenAll(task, _whenCompleted);
            return true;
        }
    }

    public Task Complete()
    {
        if (_isCompleted)
            return _whenCompleted;

        lock (_lock) {
            _isCompleted = true;
            return _whenCompleted;
        }
    }
}
