using Stl.CommandR.Internal;

namespace ActualChat.UI.Blazor;

public static class UICommanderExt
{
    public static void CancelUpdateDelays(this UICommander uiCommander)
    {
        var command = new LocalActionCommand() { Handler = static _ => Task.CompletedTask };
        uiCommander.Run(command, CancellationToken.None);
    }

    public static void CancelUpdateDelays(this UICommander uiCommander, Func<Task> untilTaskFactory)
    {
        var command = new LocalActionCommand() { Handler = _ => untilTaskFactory.Invoke() };
        uiCommander.Run(command, CancellationToken.None);
    }

    public static void Error(this UICommander uiCommander, Exception error)
    {
        var command = new LocalActionCommand() { Handler = _ => throw error };
        uiCommander.Run(command, CancellationToken.None);
    }
}
