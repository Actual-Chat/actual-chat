using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// <see cref="ExternalMapOpener"/> backed by the OS: it opens the point via the MAUI map intent
/// and falls back to the Google Maps link. Listing and launching the map apps installed
/// on the device is platform-specific, so it's up to the descendants - and the platforms
/// that have none (Windows) use this class as is.
/// </summary>
public class MauiMapOpener(UIHub hub) : ExternalMapOpener(hub)
{
    public override async Task Open(GeoPoint point, string? name = null)
    {
        // MKMapItem.OpenMaps (iOS/Mac Catalyst) requires the main thread, and MAUI doesn't dispatch on its own.
        try {
            var location = new MauiLocation(point.Latitude, point.Longitude);
            var options = new MapLaunchOptions { Name = name ?? "" };
            if (await DispatchToMainThread(() => Map.Default.TryOpenAsync(location, options)).ConfigureAwait(false))
                return;
        }
        catch (Exception e) {
            Log.LogError(e, "Open: failed to open {Point} in the OS map app", point.ToDisplayText());
        }

        await base.Open(point, name).ConfigureAwait(false);
    }

    public override async Task Open(MapApp mapApp, GeoPoint point, string? name = null)
    {
        try {
            // TODO: is main thread required for all of the platforms?
            if (await DispatchToMainThread(() => TryOpen(mapApp, point, name)).ConfigureAwait(false))
                return;
        }
        catch (Exception e) {
            Log.LogError(e, "Open: failed to open {Point} in {MapAppId}", point.ToDisplayText(), mapApp.Key);
        }

        await Open(point, name).ConfigureAwait(false);
    }

    // Protected methods

    // Virtual rather than abstract: the platforms with no map app support of their own
    // (Windows) use this class as is, and this default is what they need.
    protected virtual Task<bool> TryOpen(MapApp mapApp, GeoPoint point, string? name)
        => ActualLab.Async.TaskExt.FalseTask;
}
