using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;
public abstract class MauiLocationTrackerBase(AppUIHub hub) : LocationTrackerBase(hub)
{
    protected override async Task<GeoFix?> Fetch(bool mustBeFresh, CancellationToken cancellationToken)
    {
        try {
            if (!mustBeFresh) {
                var cached = await Geolocation.Default.GetLastKnownLocationAsync().ConfigureAwait(false);
                if (cached is not null)
                    return cached.ToGeoFix();
            }

            var accuracy = await GetGeolocationAccuracy(cancellationToken).ConfigureAwait(false);
            var request = new GeolocationRequest(accuracy, Constants.Location.GetTimeout);
            var location = await Geolocation.Default.GetLocationAsync(request, cancellationToken).ConfigureAwait(false);
            return location?.ToGeoFix();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // Location services off or the permission revoked mid-session: MAUI throws here,
            // while "no fix" is null everywhere else on this path, so it's reported the same way
            Log.LogWarning(e, "Fetch failed");
            SetError(ToTrackingError(e));
            return null;
        }
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
