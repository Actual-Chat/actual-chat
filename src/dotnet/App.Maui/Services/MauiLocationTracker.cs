using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// <see cref="ILocationTracker"/> backed by MAUI Essentials <see cref="Geolocation"/>.
/// Foreground-only (no background-service integration), so it's used on platforms that
/// don't need background sharing (Windows); Android/iOS use native background-capable trackers.
/// </summary>
public sealed class MauiLocationTracker(AppUIHub hub) : MauiLocationTrackerBase(hub)
{
    private readonly IGeolocation _geolocation = Geolocation.Default;

    public override async Task Start(CancellationToken cancellationToken)
    {
        if (IsTracking)
            return;

        IsTracking = true;
        _geolocation.LocationChanged += OnLocationChanged;
        var accuracy = await GetGeolocationAccuracy(cancellationToken).ConfigureAwait(false);
        var request = new GeolocationListeningRequest(accuracy, Constants.Location.UpdatePeriod);
        try {
            if (!await _geolocation.StartListeningForegroundAsync(request).ConfigureAwait(false))
                throw StandardError.External("Geolocation listening didn't start.");
        }
        catch (Exception e) {
            Log.LogError(e, "Start: failed to start geolocation listening");
            // Roll back so a later Start can retry from a clean state.
            IsTracking = false;
            _geolocation.LocationChanged -= OnLocationChanged;
            throw;
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
