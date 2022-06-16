namespace ActualChat;

public static class DisposableExt
{
    public static void DisposeSilently(this IDisposable? disposable)
    {
        if (disposable == null)
            return;
        try {
            disposable.Dispose();
        }
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception.
        catch (Exception) {
            // Intended
        }
#pragma warning restore RCS1075 // Avoid empty catch clause that catches System.Exception.
    }
}
