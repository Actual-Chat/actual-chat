using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

// A convenience helper to speed up & simplify access to history-related services
public sealed class HistoryHub : IServiceProvider, IHasServices
{
    private Session? _session;
    private Dispatcher? _dispatcher;
    private HistoryUI? _historyUI;

    // Some handy internal shortcuts
    internal HistoryItemIdFormatter ItemIdFormatter { get; }

    public IServiceProvider Services { get; }
    public Session Session => _session ??= Services.GetRequiredService<Session>();
    public HostInfo HostInfo { get; }
    public UrlMapper UrlMapper { get; }
    public NavigationManager Nav { get; }
    public Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    public IJSRuntime JS { get; }
    public HistoryUI HistoryUI => _historyUI ??= Services.GetRequiredService<HistoryUI>();

    public HistoryHub(IServiceProvider services)
    {
        Services = services;
        ItemIdFormatter = services.GetRequiredService<HistoryItemIdFormatter>();
        HostInfo = services.GetRequiredService<HostInfo>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        Nav = services.GetRequiredService<NavigationManager>();
        JS = services.GetRequiredService<IJSRuntime>();
    }

    public object? GetService(Type serviceType)
        => Services.GetService(serviceType);
}
