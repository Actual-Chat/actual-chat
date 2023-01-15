namespace ActualChat;

public static class CancellationTokenSourceExt
{
    private static readonly Func<CancellationTokenSource, bool> IsDisposedGetter;

    static CancellationTokenSourceExt()
    {
        var tCts = typeof(CancellationTokenSource);
        var fIsDisposed =
            tCts.GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? tCts.GetField("m_disposed", BindingFlags.Instance | BindingFlags.NonPublic);
        IsDisposedGetter = fIsDisposed!.GetGetter<CancellationTokenSource, bool>();
    }

    public static CancellationToken GetTokenOrCancelled(this CancellationTokenSource cancellationTokenSource)
    {
        try {
            if (cancellationTokenSource.IsCancellationRequested || IsDisposedGetter(cancellationTokenSource))
                return CancellationTokenExt.Cancelled;
            return cancellationTokenSource.Token;
        }
        catch (ObjectDisposedException) {
            return CancellationTokenExt.Cancelled;
        }
    }
}
