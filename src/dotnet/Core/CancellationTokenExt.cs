namespace ActualChat;

public static class CancellationTokenExt
{
    private static CancellationTokenSource CancelledTokenSource { get; }

    public static CancellationToken Cancelled { get; }

    static CancellationTokenExt()
    {
        CancelledTokenSource = new CancellationTokenSource();
        CancelledTokenSource.Cancel();
        Cancelled = CancelledTokenSource.Token;
    }
}
