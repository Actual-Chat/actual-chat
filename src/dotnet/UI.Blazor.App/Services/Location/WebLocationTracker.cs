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
        SetError(null);
        _blazorRef = DotNetObjectReference.Create(this);
        try {
            _jsRef = await _hub.JS
                .InvokeAsync<IJSObjectReference>(JSStartMethod, cancellationToken, _blazorRef)
                .ConfigureAwait(false);
        }
        catch {
            // Roll back so a later Start can retry from a clean state.
            IsTracking = false;
            _blazorRef.DisposeSilently();
            _blazorRef = null;
            throw;
        }
    }

    public override async Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return;

        SetCached(null);
        SetError(null);
        await DisposeWatch().ConfigureAwait(false);
    }

    [JSInvokable]
    public void OnLocation(double latitude, double longitude, double? accuracy, double? heading)
        => SetCached(new GeoPoint(latitude, longitude, (float?)accuracy, (float?)heading));

    [JSInvokable]
    public void OnError(int code)
    {
        var error = code is >= 1 and <= 3 ? (GeoTrackingError)code : GeoTrackingError.PositionUnavailable;
        // W3C removes a denied watch and never resumes it, so drop it here (a later Start re-arms);
        // PositionUnavailable/Timeout keep the watch, which resumes on its own once a fix returns.
        if (error == GeoTrackingError.PermissionDenied)
            _ = DisposeWatch();
        SetError(error);
    }

    // Protected/internal methods

    protected override Task<GeoPoint?> Fetch(bool mustBeFresh, CancellationToken cancellationToken)
        => _hub.JS
            .InvokeAsync<GeoPoint?>(JSGetCurrentMethod, cancellationToken, mustBeFresh)
            .AsTask();

    // Private methods

    private async Task DisposeWatch()
    {
        IsTracking = false;
        if (_jsRef is { } jsRef) {
            _jsRef = null;
            await jsRef.DisposeSilentlyAsync("stop").ConfigureAwait(false);
        }
        _blazorRef.DisposeSilently();
        _blazorRef = null;
    }
}
