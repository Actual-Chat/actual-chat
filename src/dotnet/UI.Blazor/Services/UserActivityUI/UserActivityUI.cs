using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Tracks user interaction events to determine online presence activity status.
/// </summary>
public class UserActivityUI : UIServiceBase<UIHub>, IUserActivityUIBackend
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.UserActivityUI.init";

    private readonly MutableState<Moment> _activeUntil;
    private readonly MutableState<Moment> _lastPresentAt;

    private MomentClock CpuClock { get; }
    private Moment CpuNow => CpuClock.Now;

    public IState<Moment> ActiveUntil => _activeUntil; // CPU time

    // CPU time of the last interaction that happened while the document was visible;
    // Moment.MinValue while it's hidden. Consumers apply their own grace period on top -
    // "the user is at the screen" means something different for presence and for reading.
    public IState<Moment> LastPresentAt => _lastPresentAt;

    public UserActivityUI(UIHub hub) : base(hub)
    {
        CpuClock = Clocks.CpuClock;
        _activeUntil = StateFactory.NewMutable(
            CpuNow + Constants.Presence.CheckPeriod,
            nameof(ActiveUntil));
        _lastPresentAt = StateFactory.NewMutable(
            CpuNow,
            nameof(LastPresentAt));
        var blazorRef = DotNetObjectReference.Create<IUserActivityUIBackend>(this);
        Hub.RegisterDisposable(blazorRef);
        _ = JS.InvokeVoidAsync(JSInitMethod, blazorRef,
            Constants.Presence.ActivityPeriod.TotalMilliseconds,
            Constants.Presence.CheckPeriod.TotalMilliseconds);
    }

    [JSInvokable]
    public void OnInteraction(double willBeActiveForMs)
    {
        // Zero means the document just went hidden - the JS side reports it explicitly
        // rather than letting the activity period decay, so presence drops right away.
        var activeFor = TimeSpan.FromMilliseconds(willBeActiveForMs);
        var now = CpuNow;
        _activeUntil.Value = now + activeFor;
        _lastPresentAt.Value = activeFor > TimeSpan.Zero ? now : Moment.MinValue;
    }
}
