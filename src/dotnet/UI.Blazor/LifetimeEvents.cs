namespace ActualChat.UI.Blazor;

public class LifetimeEvents
{
    public event Action<IServiceProvider> OnCircuitContextCreated = _ => { };
    public event Action<IServiceProvider> OnAppInitialized = _ => { };

    internal void RaiseOnCircuitContextCreated(IServiceProvider services)
        => OnCircuitContextCreated.Invoke(services);

    public void RaiseOnAppInitialized(IServiceProvider services)
        => OnAppInitialized.Invoke(services);
}
