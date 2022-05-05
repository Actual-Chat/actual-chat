using Stl.Fusion.UI;

namespace ActualChat;

public static class UICommandRunnerExt
{
    public static async Task<Result<T>> AsResult<T>(this Task<(T Result, UICommandEvent CommandEvent)> runTask)
    {
        try {
            var (_, completedEvent) = await runTask.ConfigureAwait(false);
            return completedEvent.Result!.Cast<T>();
        }
        catch (Exception e) {
            return Result.Error<T>(e);
        }
    }
}
