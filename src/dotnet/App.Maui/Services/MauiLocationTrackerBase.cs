using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;
public abstract class MauiLocationTrackerBase(AppUIHub hub) : LocationTrackerBase(hub)
{
    public override async Task<GeoPoint?> Get(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && await Geolocation.Default.GetLastKnownLocationAsync().ConfigureAwait(false) is { } cached)
            return cached.ToGeoPoint();

        var accuracy = await GetGeolocationAccuracy(cancellationToken).ConfigureAwait(false);
        var request = new GeolocationRequest(accuracy, Constants.Location.GetTimeout);
        var location = await Geolocation.Default.GetLocationAsync(request, cancellationToken).ConfigureAwait(false);
        return location?.ToGeoPoint();
    }

    protected async Task<GeolocationAccuracy> GetGeolocationAccuracy(CancellationToken cancellationToken)
        => await GetAccuracy(cancellationToken).ConfigureAwait(false) switch {
            GeoTrackingAccuracy.High => GeolocationAccuracy.Best,
            GeoTrackingAccuracy.Low => GeolocationAccuracy.Low,
            _ => GeolocationAccuracy.Medium,
        };

    protected static GeoTrackingError ToTrackingError(Exception error)
        => error is PermissionException
            ? GeoTrackingError.PermissionDenied
            : GeoTrackingError.PositionUnavailable;
}
