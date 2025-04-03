namespace ActualChat;

public static class CancellationTokenExt
{
    public static async Task WhenCanceled(this CancellationToken cancellationToken)
    {
        using var dTask = cancellationToken.ToTask();
        await dTask.Resource.ConfigureAwait(false);
    }
}
