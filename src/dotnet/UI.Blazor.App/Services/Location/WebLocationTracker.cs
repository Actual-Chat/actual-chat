using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class WebLocationTracker(AppUIHub hub) : LocationTrackerBase(hub)
{
    private static readonly string JSStartMethod = $"{BlazorUIAppModule.ImportName}.LocationTracker.start";
    private static readonly string JSGetCurrentMethod = $"{BlazorUIAppModule.ImportName}.LocationTracker.getCurrent";

    private readonly AppUIHub _hub = hub;
    private DotNetObjectReference<WebLocationTracker>? _blazorRef;
    private IJSObjectReference? _jsRef;

    public override async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        _blazorRef = DotNetObjectReference.Create(this);
        _jsRef = await _hub.JS
            .InvokeAsync<IJSObjectReference>(JSStartMethod, cancellationToken, _blazorRef)
            .ConfigureAwait(false);
    }

    public override async Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return;

        IsTracking = false;
        SetLocation(null);
        if (_jsRef is { } jsRef) {
            _jsRef = null;
            await jsRef.DisposeSilentlyAsync("stop").ConfigureAwait(false);
        }
        _blazorRef?.Dispose();
        _blazorRef = null;
    }

    [JSInvokable]
    public void OnLocation(double latitude, double longitude, double? accuracy, double? heading)
        => SetLocation(new GeoPoint(latitude, longitude, (float?)accuracy, (float?)heading));

    public override Task<GeoPoint?> Get(CancellationToken cancellationToken)
        => _hub.JS
            .InvokeAsync<GeoPoint?>(JSGetCurrentMethod, cancellationToken)
            .AsTask();
}
