using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class UserActivityUI : ScopedServiceBase<UIHub>, IUserActivityUIBackend
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.UserActivityUI.init";

    private readonly IMutableState<Moment> _activeUntil;

    private IJSRuntime JS => Hub.JSRuntime();
    private IMomentClock CpuClock { get; }
    private Moment CpuNow => CpuClock.Now;

    public IState<Moment> ActiveUntil => _activeUntil; // CPU time

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UserActivityUI))]
    public UserActivityUI(UIHub hub) : base(hub)
    {
        CpuClock = Clocks.CpuClock;
        _activeUntil = StateFactory.NewMutable(
            CpuNow + Constants.Presence.CheckPeriod,
            nameof(ActiveUntil));
        var blazorRef = DotNetObjectReference.Create<IUserActivityUIBackend>(this);
        Hub.RegisterDisposable(blazorRef);
        _ = JS.InvokeVoidAsync(JSInitMethod, blazorRef,
            Constants.Presence.ActivityPeriod.TotalMilliseconds,
            Constants.Presence.CheckPeriod.TotalMilliseconds);
    }

    [JSInvokable]
    public void OnInteraction(double willBeActiveForMs)
        => _activeUntil.Value = CpuNow + TimeSpan.FromMilliseconds(willBeActiveForMs);
}
