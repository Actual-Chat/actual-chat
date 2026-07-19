namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform location source for live sharing: <see cref="Start"/> begins delivering
/// positions into <see cref="LastKnown"/>, <see cref="Stop"/> ends them and clears it.
/// <see cref="Error"/> is non-null while tracking is unavailable (e.g. permission revoked).
/// </summary>
public interface ILocationTracker
{
    IState<GeoPoint?> LastKnown { get; }
    IState<GeoTrackingError?> Error { get; }

    Task<GeoPoint?> Get(bool force = false, CancellationToken cancellationToken = default);
    Task Start(CancellationToken cancellationToken);
    Task Stop(CancellationToken cancellationToken);
}
