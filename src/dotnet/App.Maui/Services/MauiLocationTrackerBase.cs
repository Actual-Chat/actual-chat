using ActualChat.UI.Blazor.App.Services;
using Microsoft.Maui.Devices.Sensors;

namespace ActualChat.App.Maui.Services;
public abstract class MauiLocationTrackerBase(AppUIHub hub) : LocationTrackerBase(hub)
{
    public override async Task<GeoPoint?> Get(CancellationToken cancellationToken)
    {
        var settings = await hub.LocalSettings.LocalAppSettings().Get(cancellationToken).ConfigureAwait(false);
        var accuracy = settings.LocationAccuracyOrDefault switch {
            GeoTrackingAccuracy.High => GeolocationAccuracy.Best,
            GeoTrackingAccuracy.Low => GeolocationAccuracy.Low,
            _ => GeolocationAccuracy.Medium,
        };
        var request = new GeolocationRequest(accuracy, Constants.Location.GetTimeout);
        var location = await Geolocation.Default.GetLocationAsync(request, cancellationToken).ConfigureAwait(false);
        return location?.ToGeoPoint();
    }
}
