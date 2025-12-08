namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public abstract partial class AppActivity : UIWorkerBase<UIHub>, IComputeService
{
    private readonly MutableState<ActivityState> _state;

    protected BackgroundStateTracker BackgroundStateTracker
        => field ??= Services.GetRequiredService<BackgroundStateTracker>();

    public IState<ActivityState> State => _state;

    protected AppActivity(UIHub hub) : base(hub)
        => _state = StateFactory.NewMutable(
            ActivityState.Foreground,
            StateCategories.Get(typeof(AppActivity), nameof(State)));

    [ComputeMethod]
    protected abstract Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken);
}
