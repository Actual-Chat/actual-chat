namespace ActualChat;

public static class CancellationTokenExt
{
    private static CancellationTokenSource CancelledTokenSource { get; }
    private static CancellationSource CancelledSource { get; }

    public static CancellationToken Cancelled { get; }

    static CancellationTokenExt()
    {
        CancelledTokenSource = new CancellationTokenSource();
        CancelledSource = new CancellationSource(CancelledTokenSource);
        CancelledTokenSource.Cancel();
        Cancelled = CancelledTokenSource.Token;
    }
}
