
namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public abstract partial class BackgroundActivityUI : UIWorkerBase<UIHub>, IComputeService
{
    private readonly MutableState<BackgroundActivityState> _state;

    protected BackgroundStateTracker BackgroundStateTracker
        => field ??= Services.GetRequiredService<BackgroundStateTracker>();

    public IState<BackgroundActivityState> State => _state;
    public IState<bool> IsRunningInBackground => BackgroundStateTracker.IsBackground;

    protected BackgroundActivityUI(UIHub hub) : base(hub)
        => _state = StateFactory.NewMutable(
            BackgroundActivityState.Foreground,
            StateCategories.Get(typeof(BackgroundActivityUI), nameof(State)));

    [ComputeMethod]
    protected abstract Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken);
}
