using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

// A convenience helper to speed up & simplify access to history-related services
public sealed class HistoryHub : IServiceProvider, IHasServices
{
    private Dispatcher? _dispatcher;
    private HistoryUI? _historyUI;

    // Some handy internal shortcuts
    internal HistoryPositionFormatter PositionFormatter { get; }

    public IServiceProvider Services { get; }
    public HistoryUI HistoryUI => _historyUI ??= Services.GetRequiredService<HistoryUI>();
    public HostInfo HostInfo { get; }
    public Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    public IJSRuntime JS { get; }
    public NavigationManager Nav { get; }

    public HistoryHub(IServiceProvider services)
    {
        Services = services;
        HostInfo = services.GetRequiredService<HostInfo>();
        JS = services.GetRequiredService<IJSRuntime>();
        Nav = services.GetRequiredService<NavigationManager>();
        PositionFormatter = services.GetRequiredService<HistoryPositionFormatter>();
    }

    public object? GetService(Type serviceType)
        => Services.GetService(serviceType);
}
