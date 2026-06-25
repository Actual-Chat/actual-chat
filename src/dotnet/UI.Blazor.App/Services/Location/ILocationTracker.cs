namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform location source for live sharing: <see cref="Start"/> begins delivering
/// positions into <see cref="LastKnown"/>, <see cref="Stop"/> ends them and clears it.
/// </summary>
public interface ILocationTracker
{
    IState<GeoPoint?> LastKnown { get; }
    // TODO: remove if not used
    bool IsTracking { get; }

    Task Start(CancellationToken cancellationToken);
    Task Stop(CancellationToken cancellationToken);
}
