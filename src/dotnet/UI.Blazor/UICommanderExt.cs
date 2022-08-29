using Stl.CommandR.Internal;

namespace ActualChat.UI.Blazor;

public static class UICommanderExt
{
    public static void CancelUpdateDelays(this UICommander uiCommander)
    {
        var cmd = new LocalActionCommand() { Handler = static _ => Task.CompletedTask };
        uiCommander.Run(cmd, CancellationToken.None);
    }
}
