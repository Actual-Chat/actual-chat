namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public abstract partial class AppActivity : ScopedWorkerBase, IComputeService
{
    private readonly IMutableState<ActivityState> _state;
    private BackgroundStateTracker? _backgroundStateTracker;

    protected BackgroundStateTracker BackgroundStateTracker
        => _backgroundStateTracker ??= Services.GetRequiredService<BackgroundStateTracker>();

    public IState<ActivityState> State => _state;

    protected AppActivity(IServiceProvider services) : base(services)
        => _state = StateFactory.NewMutable(
            ActivityState.Foreground,
            StateCategories.Get(typeof(AppActivity), nameof(State)));

    [ComputeMethod]
    protected abstract Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken);
}
