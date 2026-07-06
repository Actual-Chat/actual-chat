namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Why the platform <see cref="ILocationTracker"/> can't deliver positions.
/// Values match the W3C GeolocationPositionError codes.
/// </summary>
public enum GeoTrackingError
{
    PermissionDenied = 1,
    PositionUnavailable = 2,
    Timeout = 3,
}
