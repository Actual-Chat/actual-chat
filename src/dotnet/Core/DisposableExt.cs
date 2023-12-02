namespace ActualChat;

public static class DisposableExt
{
    public static ValueTask DisposeUnknownSilently(object? disposable)
    {
        if (disposable is IAsyncDisposable ad)
            return ad.DisposeSilentlyAsync();
        if (disposable is IDisposable d)
            d.DisposeSilently();
        return default;
    }

    public static ValueTask DisposeUnknown(object? disposable)
    {
        if (disposable is IAsyncDisposable ad)
            return ad.DisposeAsync();
        if (disposable is IDisposable d)
            d.Dispose();
        return default;
    }

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

    public static async ValueTask DisposeSilentlyAsync(this IAsyncDisposable? disposable)
    {
        if (disposable == null)
            return;
        try {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception.
        catch (Exception) {
            // Intended
        }
#pragma warning restore RCS1075 // Avoid empty catch clause that catches System.Exception.
    }
}
