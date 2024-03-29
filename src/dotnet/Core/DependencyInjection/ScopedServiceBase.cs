using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualLab.Diagnostics;

namespace ActualChat.DependencyInjection;

public abstract class ScopedServiceBase<THub>(THub hub) : IHasIsDisposed
    where THub : Hub
{
    private ILogger? _log;

    public THub Hub { get; } = hub;
    public bool IsDisposed => Hub.IsDisposed;

    protected IServiceProvider Services {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.Services;
    }

    protected HostInfo HostInfo {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.HostInfo();
    }

    protected Session Session {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.Session();
    }

    protected IStateFactory StateFactory {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.StateFactory();
    }

    protected AccountSettings AccountSettings {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.AccountSettings();
    }

    protected LocalSettings LocalSettings {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.LocalSettings();
    }

    protected Features Features {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.Features();
    }

    protected UrlMapper UrlMapper {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.UrlMapper();
    }

    protected ICommander Commander {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.Commander();
    }

    protected MomentClockSet Clocks {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hub.Clocks();
    }

    protected ILogger Log => _log ??= Hub.LoggerFactory().CreateLogger(GetType().NonProxyType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);
}
