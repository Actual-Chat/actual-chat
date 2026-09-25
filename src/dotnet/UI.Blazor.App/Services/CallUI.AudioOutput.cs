using ActualChat.Live;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    // The output picked for the call on the line; null is the platform's default. The pick is this
    // call's only, and a device that arrives mid-call takes over: plugging something in says where
    // you want to listen.
    private readonly MutableState<string?> _pickedOutputRouteId;

    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;

    // Public methods

    [ComputeMethod]
    public virtual async Task<AudioOutputRoutes> GetOutputRoutes(CancellationToken cancellationToken)
    {
        if (AudioFocusUI.OutputRoutes is not { } outputRoutes)
            return AudioOutputRoutes.None;

        var routes = await outputRoutes.Use(cancellationToken).ConfigureAwait(false);
        var pickedId = await _pickedOutputRouteId.Use(cancellationToken).ConfigureAwait(false);
        // Shown as picked from the tap itself; the platform's view catches up once the switch lands.
        return pickedId is not null && routes.Routes.Any(x => x.Id == pickedId)
            ? routes with { CurrentId = pickedId }
            : routes;
    }

    public void SelectOutputRoute(string routeId)
        => _pickedOutputRouteId.Value = routeId;

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<string?> GetOutputRouteToApply(CancellationToken cancellationToken)
    {
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Phase: CallPhase.Active or CallPhase.Dialing })
            return null;

        return await _pickedOutputRouteId.Use(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task SyncOutputRoute(CancellationToken cancellationToken)
    {
        var cRouteId = await Computed
            .Capture(() => GetOutputRouteToApply(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        string? routeId = null;
        await foreach (var c in cRouteId.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.Value == routeId)
                continue;

            routeId = c.Value;
            await AudioFocusUI.ApplyOutputRoute(routeId).ConfigureAwait(false);
        }
    }

    private async Task SyncOutputRouteTakeover(CancellationToken cancellationToken)
    {
        if (AudioFocusUI.OutputRoutes is not { } outputRoutes)
            return;

        var cRoutes = await Computed
            .Capture(() => outputRoutes.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var externalIds = new HashSet<string>();
        await foreach (var c in cRoutes.Changes(cancellationToken).ConfigureAwait(false)) {
            var arrived = c.Value.Routes.Where(x => x.IsExternal && externalIds.Add(x.Id)).ToList();
            externalIds.IntersectWith(c.Value.Routes.Select(x => x.Id));
            if (arrived.Count == 0 || _pickedOutputRouteId.Value is null)
                continue;

            Log.LogInformation("Output {Ids} arrived mid-call, dropping the pick", arrived.Select(x => x.Id));
            _pickedOutputRouteId.Value = null;
        }
    }
}
