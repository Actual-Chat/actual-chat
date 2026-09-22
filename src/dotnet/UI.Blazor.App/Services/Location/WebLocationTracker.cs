using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class WebLocationTracker(AppUIHub hub) : LocationTrackerBase(hub)
{
    private static readonly string JSStartMethod = $"{BlazorUIAppModule.ImportName}.LocationTracker.start";
    private static readonly string JSGetCurrentMethod = $"{BlazorUIAppModule.ImportName}.LocationTracker.getCurrent";
    private static readonly string JSStartHeadingMethod = $"{BlazorUIAppModule.ImportName}.HeadingTracker.start";

    private readonly AppUIHub _hub = hub;
    private DotNetObjectReference<WebLocationTracker>? _blazorRef;
    private IJSObjectReference? _jsRef;
    private DotNetObjectReference<WebLocationTracker>? _headingBlazorRef;
    private Task<IJSObjectReference?>? _headingJSRefTask;

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
    public void OnLocation(double latitude, double longitude, double? accuracy, double? heading, double timestamp)
        => SetCached(new JsGeoFix(latitude, longitude, accuracy, heading, timestamp).ToGeoFix());

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

    [JSInvokable]
    public void OnHeading(double heading)
        => SetHeading((float)heading);

    // Protected/internal methods

    protected override void StartHeadingUpdates()
    // TODO: why so complicated via task? why it can't be just an async method?
        => _headingJSRefTask = StartHeadingWatch();

    protected override void StopHeadingUpdates()
    {
        if (_headingJSRefTask is not { } headingJSRefTask)
            return;

        _headingJSRefTask = null;
        _ = StopHeadingWatch(headingJSRefTask);
    }

    protected override async Task<GeoFix?> Fetch(bool mustBeFresh, CancellationToken cancellationToken)
    {
        var jsFix = await _hub.JS
            .InvokeAsync<JsGeoFix?>(JSGetCurrentMethod, cancellationToken, mustBeFresh)
            .ConfigureAwait(false);
        return jsFix?.ToGeoFix();
    }

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

    private async Task<IJSObjectReference?> StartHeadingWatch()
    {
        _headingBlazorRef ??= DotNetObjectReference.Create(this);
        try {
            return await _hub.JS
                .InvokeAsync<IJSObjectReference>(JSStartHeadingMethod, CancellationToken.None, _headingBlazorRef)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "StartHeadingWatch failed");
            return null;
        }
    }

    private static async Task StopHeadingWatch(Task<IJSObjectReference?> headingJSRefTask)
    {
        // Awaits the start first, so a stop issued while it's in flight still removes the listener.
        if (await headingJSRefTask.ConfigureAwait(false) is { } jsRef)
            await jsRef.DisposeSilentlyAsync("stop").ConfigureAwait(false);
    }

    // Nested types

    private sealed record JsGeoFix(
        double Latitude,
        double Longitude,
        double? Accuracy,
        double? Bearing,
        double Timestamp)
    {
        public GeoFix ToGeoFix()
            => new (
                new GeoPoint(Latitude, Longitude, (float?)Accuracy, (float?)Bearing),
                new Moment(TimeSpan.FromMilliseconds(Timestamp)));
    }
}
