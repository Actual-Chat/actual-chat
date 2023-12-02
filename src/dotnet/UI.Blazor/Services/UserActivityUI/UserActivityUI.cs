using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class UserActivityUI : ScopedServiceBase, IUserActivityUIBackend
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.UserActivityUI.init";

    private readonly IMutableState<Moment> _activeUntil;
    private DotNetObjectReference<IUserActivityUIBackend> _blazorRef;

    private IJSRuntime JS { get; }
    private IMomentClock Clock { get; }
    private Moment Now => Clock.Now;

    public IState<Moment> ActiveUntil => _activeUntil; // CPU time

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UserActivityUI))]
    public UserActivityUI(IServiceProvider services) : base(services)
    {
        JS = services.JSRuntime();
        Clock = Clocks.CpuClock;
        _activeUntil = StateFactory.NewMutable(
            Now + Constants.Presence.CheckPeriod,
            nameof(ActiveUntil));
        _blazorRef = DotNetObjectReference.Create<IUserActivityUIBackend>(this);
        Scope.RegisterDisposable(_blazorRef);
        _ = JS.InvokeVoidAsync(JSInitMethod, _blazorRef,
            Constants.Presence.ActivityPeriod.TotalMilliseconds,
            Constants.Presence.CheckPeriod.TotalMilliseconds);
    }

    [JSInvokable]
    public void OnInteraction(double willBeActiveForMs)
        => _activeUntil.Value = Now + TimeSpan.FromMilliseconds(willBeActiveForMs);
}
