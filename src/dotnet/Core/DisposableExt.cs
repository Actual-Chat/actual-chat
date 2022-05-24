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
        catch (Exception) {
            // Intended
        }
    }

}
