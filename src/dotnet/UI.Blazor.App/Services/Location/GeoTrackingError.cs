namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Why the platform <see cref="ILocationTracker"/> can't deliver positions.
/// Failure values match the W3C GeolocationPositionError codes.
/// </summary>
public enum GeoTrackingError
{
    None = 0,
    PermissionDenied = 1,
    PositionUnavailable = 2,
    Timeout = 3,
}
