namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform location source for live sharing: <see cref="Start"/> begins tracking positions and
/// <see cref="Stop"/> ends it, <see cref="Get"/> returns the tracked fix or queries the platform
/// when there's none. <see cref="Heading"/> is the compass heading, tracked only while
/// <see cref="WatchHeading"/> runs. <see cref="Error"/> is non-null while tracking is unavailable.
/// </summary>
public interface ILocationTracker
{
    IState<GeoTrackingError?> Error { get; }
    IState<float?> Heading { get; }

    Task<GeoFix?> Get(bool mustBeFresh = false, CancellationToken cancellationToken = default);
    Task Start(CancellationToken cancellationToken);
    Task Stop(CancellationToken cancellationToken);
    Task WatchHeading(CancellationToken cancellationToken);
}
