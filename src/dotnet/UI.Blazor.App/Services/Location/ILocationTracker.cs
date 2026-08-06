namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform location source for live sharing: <see cref="Start"/> begins tracking positions and
/// <see cref="Stop"/> ends it, <see cref="Get"/> returns the tracked fix or queries the platform
/// when there's none. <see cref="Error"/> is non-null while tracking is unavailable.
/// </summary>
public interface ILocationTracker
{
    IState<GeoTrackingError?> Error { get; }

    Task<GeoFix?> Get(bool mustBeFresh = false, CancellationToken cancellationToken = default);
    Task Start(CancellationToken cancellationToken);
    Task Stop(CancellationToken cancellationToken);
}
