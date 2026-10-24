namespace ActualChat.UI.Blazor.Services;

public partial class DebugUI
{
    [JSInvokable]
    public bool SuspendCommandQueue(bool suspend = true)
    {
        var queue = Hub.CommandQueueUI;
        if (suspend)
            queue.Pause();
        else
            queue.Resume();
        Log.LogInformation("SuspendCommandQueue({Suspend}): isSuspended = {IsSuspended}", suspend, queue.IsPaused);
        return queue.IsPaused;
    }
}
