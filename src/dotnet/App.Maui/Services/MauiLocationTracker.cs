using ActualChat.UI.Blazor.App.Services;
using Microsoft.Maui.Devices.Sensors;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// <see cref="ILocationTracker"/> backed by MAUI Essentials <see cref="Geolocation"/>.
/// Foreground-only (no background-service integration), so it's used on platforms that
/// don't need background sharing (Windows); Android/iOS use native background-capable trackers.
/// </summary>
public sealed class MauiLocationTracker : MauiLocationTrackerBase
{
    private readonly IGeolocation _geolocation = Geolocation.Default;
    private ILogger Log { get; }

    public MauiLocationTracker(AppUIHub hub) : base(hub)
        => Log = hub.Services.LogFor(GetType());

    public override async Task Start(CancellationToken cancellationToken)
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

    public override Task Stop(CancellationToken cancellationToken)
    {
        if (!IsTracking)
            return Task.CompletedTask;

        IsTracking = false;
        SetLocation(null);
        _geolocation.LocationChanged -= OnLocationChanged;
        _geolocation.StopListeningForeground();
        return Task.CompletedTask;
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
        => SetLocation(e.Location.ToGeoPoint());
}
