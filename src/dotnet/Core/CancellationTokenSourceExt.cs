using System.Reflection;
using Stl.Reflection;

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

    public static CancellationToken GetTokenOrDefault(this CancellationTokenSource cancellationTokenSource)
    {
        try {
            if (cancellationTokenSource.IsCancellationRequested || IsDisposedGetter(cancellationTokenSource))
                return default;
            return cancellationTokenSource.Token;
        }
        catch (ObjectDisposedException) {
            return default;
        }
    }
}
