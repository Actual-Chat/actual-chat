using Stl.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public abstract partial class AppActivity(IServiceProvider services) : WorkerBase, IComputeService
{
    private readonly IMutableState<ActivityState> _state = services
        .StateFactory()
        .NewMutable(
            ActivityState.Foreground,
            StateCategories.Get(typeof(AppActivity), nameof(State)));
    private BackgroundStateTracker? _backgroundStateTracker;
    private ILogger? _log;

    protected readonly IServiceProvider Services = services;
    protected BackgroundStateTracker BackgroundStateTracker
        => _backgroundStateTracker ??= Services.GetRequiredService<BackgroundStateTracker>();
    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IState<ActivityState> State => _state;

    [ComputeMethod]
    protected abstract Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken);
}
