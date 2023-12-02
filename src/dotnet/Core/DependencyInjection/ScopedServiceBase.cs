using ActualChat.Hosting;
using Stl.Diagnostics;

namespace ActualChat.DependencyInjection;

public abstract class ScopedServiceBase(Scope scope) : IHasIsDisposed
{
    private ILogger? _log;

    public bool IsDisposed => Scope.IsDisposed;

    protected Scope Scope { get; } = scope;

    protected IServiceProvider Services {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Scope.Services;
    }

    protected HostInfo HostInfo {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Scope.HostInfo;
    }

    protected MomentClockSet Clocks {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Scope.Clocks();
    }

    protected Session Session {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Scope.Session;
    }

    protected IStateFactory StateFactory {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Scope.StateFactory();
    }

    protected ILogger Log => _log ??= Scope.LoggerFactory().CreateLogger(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    protected ScopedServiceBase(IServiceProvider services)
        : this(services.Scope()) { }
}
