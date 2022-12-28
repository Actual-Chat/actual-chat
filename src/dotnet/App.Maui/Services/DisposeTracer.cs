namespace ActualChat.App.Maui.Services;

internal class DisposeTracer : IDisposable
{
    private ILogger<DisposeTracer> Log { get; }

    public DisposeTracer(IServiceProvider services)
        => Log = services.GetRequiredService<ILogger<DisposeTracer>>();

    public void Dispose()
    {
        var stackTrace = Environment.StackTrace;

#if ANDROID
        // I am trying to figure out when container is disposed.
        Log.LogDebug("Blazor app scoped container is being disposed. StackTrace: \n{StackTrace}",
            stackTrace);
#endif
    }
}
