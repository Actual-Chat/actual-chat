namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public abstract partial class AppActivity(IServiceProvider services) : ScopedWorkerBase(services), IComputeService
{
    private readonly IMutableState<ActivityState> _state = services
        .StateFactory()
        .NewMutable(
            ActivityState.Foreground,
            StateCategories.Get(typeof(AppActivity), nameof(State)));
    private BackgroundStateTracker? _backgroundStateTracker;

    protected BackgroundStateTracker BackgroundStateTracker
        => _backgroundStateTracker ??= Services.GetRequiredService<BackgroundStateTracker>();

    public IState<ActivityState> State => _state;

    [ComputeMethod]
    protected abstract Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken);
}
