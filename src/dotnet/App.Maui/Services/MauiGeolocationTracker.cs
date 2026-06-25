using ActualChat.UI.Blazor.App.Services;
using Microsoft.Maui.Devices.Sensors;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// <see cref="ILocationTracker"/> backed by MAUI Essentials <see cref="Geolocation"/>.
/// Foreground-only (no background-service integration), so it's used on platforms that
/// don't need background sharing (Windows); Android/iOS use native background-capable trackers.
/// </summary>
public sealed class MauiGeolocationTracker : ILocationTracker
{
    private readonly IGeolocation _geolocation = Geolocation.Default;
    private readonly MutableState<GeoPoint?> _lastKnown;
    private ILogger Log { get; }

    public IState<GeoPoint?> LastKnown => _lastKnown;
    public bool IsTracking { get; private set; }

    public MauiGeolocationTracker(AppUIHub hub)
    {
        _lastKnown = hub.StateFactory.NewMutable(
            (GeoPoint?)null,
            StateCategories.Get(GetType(), nameof(LastKnown)));
        Log = hub.Services.LogFor(GetType());
    }

    public async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        _geolocation.LocationChanged += OnLocationChanged;
        var request = new GeolocationListeningRequest(
            GeolocationAccuracy.Best,
            Constants.Location.UpdatePeriod);
        try {
            await _geolocation.StartListeningForegroundAsync(request).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Start: failed to start geolocation listening");
            IsTracking = false;
            _geolocation.LocationChanged -= OnLocationChanged;
        }
    }

    public Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return Task.CompletedTask;

        IsTracking = false;
        _lastKnown.Value = null;
        _geolocation.LocationChanged -= OnLocationChanged;
        _geolocation.StopListeningForeground();
        return Task.CompletedTask;
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        var location = e.Location;
        var accuracy = location.Accuracy is { } a ? (float)a : (float?)null;
        var bearing = location.Course is { } c ? (float)c : (float?)null;
        _lastKnown.Value = new GeoPoint(location.Latitude, location.Longitude, accuracy, bearing);
    }
}
