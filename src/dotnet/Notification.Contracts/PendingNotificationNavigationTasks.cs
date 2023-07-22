namespace ActualChat.Notification;

public class PendingNotificationNavigationTasks
{
    private readonly object _lock = new();
    private volatile Task _whenCompleted = Task.CompletedTask;

    public Task WhenCompleted => _whenCompleted;

    public void Add(Task task)
    {
        lock (_lock) {
            _whenCompleted = _whenCompleted.IsCompleted
                ? task
                : Task.WhenAll(task, _whenCompleted);
        }
    }
}
