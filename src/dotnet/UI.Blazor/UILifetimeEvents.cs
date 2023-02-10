namespace ActualChat.UI.Blazor;

public sealed class UILifetimeEvents
{
    public event Action<IServiceProvider>? OnCircuitContextCreated;
    public event Action<IServiceProvider>? OnAppInitialized;

    public UILifetimeEvents(IEnumerable<Action<UILifetimeEvents>> configureActions)
    {
        foreach (var configureAction in configureActions)
            configureAction.Invoke(this);
    }

    internal void RaiseOnCircuitContextCreated(IServiceProvider services)
        => OnCircuitContextCreated?.Invoke(services);

    public void RaiseOnAppInitialized(IServiceProvider services)
        => OnAppInitialized?.Invoke(services);
}
