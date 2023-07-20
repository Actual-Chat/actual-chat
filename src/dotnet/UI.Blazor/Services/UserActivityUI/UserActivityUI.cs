using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class UserActivityUI : IUserActivityUIBackend, IDisposable
{
    private readonly IMutableState<Moment> _activeUntil;
    private readonly DotNetObjectReference<IUserActivityUIBackend> _blazorRef;

    private IJSRuntime JS { get; }
    private IMomentClock Clock { get; }
    private Moment Now => Clock.Now;

    public IState<Moment> ActiveUntil => _activeUntil;

    public UserActivityUI(IServiceProvider services)
    {
        JS = services.JSRuntime();
        Clock = services.Clocks().CpuClock;
        _activeUntil = services.StateFactory().NewMutable(
            Now + Constants.Presence.CheckPeriod,
            nameof(ActiveUntil));
        _blazorRef = DotNetObjectReference.Create<IUserActivityUIBackend>(this);
        _ = JS.InvokeAsync<IJSObjectReference>(
            $"{BlazorUICoreModule.ImportName}.UserActivityUI.init",
            _blazorRef,
            Constants.Presence.ActivityPeriod.TotalMilliseconds,
            Constants.Presence.CheckPeriod.TotalMilliseconds);
    }

    public void Dispose()
        => _blazorRef.Dispose();

    [JSInvokable]
    public void OnInteraction(double willBeActiveForMs)
        => _activeUntil.Value = Now + TimeSpan.FromMilliseconds(willBeActiveForMs);
}
