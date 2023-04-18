namespace ActualChat.App.Maui;

public static class DisposeServicesHelper
{
    public static async ValueTask DisposeAsync(IServiceProvider services)
    {
        if (services is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (services is IDisposable disposable)
            disposable.Dispose();
    }
}
