namespace ActualChat.UI.Blazor.Services;

public partial class DebugUI
{
    [JSInvokable]
    public bool SuspendCommandQueue(bool suspend = true)
    {
        // The queue lives in UI.Blazor.App, which this project can't reference,
        // so the app wires the handler up in its module
        if (SuspendCommandQueueHandler is not { } handler)
            throw StandardError.NotSupported<DebugUI>("The client command queue isn't available on this host.");

        var isSuspended = handler.Invoke(suspend);
        Log.LogInformation("SuspendCommandQueue({Suspend}): isSuspended = {IsSuspended}", suspend, isSuspended);
        return isSuspended;
    }
}
