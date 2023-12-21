using Exception = System.Exception;

namespace ActualChat.UI.Blazor.Services;

public static class DispatcherExt
{
    public static async Task InvokeSafeAsync(this Dispatcher dispatcher, Action workItem, ILogger log)
    {
        try {
            await dispatcher.InvokeAsync(workItem);
        }
        catch (Exception e) {
            log.LogError(e, "Error invoking action using Dispatcher");
        }
    }
}
