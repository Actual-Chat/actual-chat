namespace ActualChat;

public static class CancellationTokenSourceExt
{
    public static void CancelAndDisposeSilently(this CancellationTokenSource cancellationTokenSource)
    {
        try {
            cancellationTokenSource.Cancel();
        }
        catch {
            // Intended
        }
        finally {
            cancellationTokenSource.Dispose();
        }
    }

}
