using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class WebLocationTracker : ILocationTracker
{
    private static readonly string JSStartMethod = $"{BlazorUIAppModule.ImportName}.LocationTracker.start";

    private readonly AppUIHub _hub;
    private readonly MutableState<GeoPoint?> _lastKnown;
    private DotNetObjectReference<WebLocationTracker>? _blazorRef;
    private IJSObjectReference? _jsRef;

    public IState<GeoPoint?> LastKnown => _lastKnown;
    public bool IsTracking { get; private set; }

    public WebLocationTracker(AppUIHub hub)
    {
        _hub = hub;
        _lastKnown = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(LastKnown)));
    }

    public async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        _blazorRef = DotNetObjectReference.Create(this);
        _jsRef = await _hub.JS
            .InvokeAsync<IJSObjectReference>(JSStartMethod, cancellationToken, _blazorRef)
            .ConfigureAwait(false);
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return;

        IsTracking = false;
        _lastKnown.Value = null;
        if (_jsRef is { } jsRef) {
            _jsRef = null;
            await jsRef.DisposeSilentlyAsync("stop").ConfigureAwait(false);
        }
        _blazorRef?.Dispose();
        _blazorRef = null;
    }

    [JSInvokable]
    public void OnLocation(double latitude, double longitude, double? accuracy, double? heading)
        => _lastKnown.Value = new GeoPoint(latitude, longitude, (float?)accuracy, (float?)heading);
}
