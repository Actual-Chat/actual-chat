namespace ActualChat.App.Maui.Services;

public class ScopedServicesDisposeTracker(IServiceProvider services) : IDisposable
{
    public void Dispose()
        => TryDiscardActiveScopedServices(services, "ScopedServicesDisposeTracker.Dispose");
}
