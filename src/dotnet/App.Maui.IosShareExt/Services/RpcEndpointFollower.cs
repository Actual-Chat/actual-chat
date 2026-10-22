using ActualChat.Maui;
using ActualChat.Rpc;
using ActualLab.Rpc;

namespace ActualChat.App.Maui.IosShareExt.Services;

/// <summary>
/// Puts a share's RPC on the endpoint the app chose, unless that one fails a check on this network.
/// A stopgap until the extension runs <see cref="RpcPathManager"/>, see #5570.
/// </summary>
public sealed class RpcEndpointFollower(IServiceProvider services) : WorkerBase
{
    private IServiceProvider Services { get; } = services;
    private IRpcPathProbe Probe => field ??= Services.GetRequiredService<IRpcPathProbe>();
    private RpcHub RpcHub => field ??= Services.RpcHub();
    private ILogger Log => field ??= Services.LogFor(GetType());

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        // No endpoint passing usually means no network yet, which can come back while the sheet is open.
        // The probe logs each failed check, so the chain doesn't log the failure again.
        => AsyncChain.From(Follow)
            .RetryForever(RetryDelaySeq.Exp(3, 30), Log)
            .RunIsolated(cancellationToken);

    // Private methods

    private async Task Follow(CancellationToken cancellationToken)
    {
        if (RpcEndpointSelector.Instance is not { } selector)
            return;

        var appEndpoint = MauiPreferences.RpcEndpoint ?? selector.OriginHost;
        if (Use(selector, appEndpoint))
            Log.LogInformation("Moved to the app's RPC endpoint: {Endpoint}", appEndpoint);

        // The app's choice may predate this network, and a capped link still connects -
        // only moving a payload through it tells, see RpcPathProbe
        var current = selector.Current;
        using var cts = cancellationToken.CreateLinkedTokenSource();
        try {
            var currentCheckTask = Probe.Check(current, cts.Token);
            var startOthersTask = Task.Delay(RpcPathPlanner.DefaultStartNextAfter, cts.Token);
            await Task.WhenAny(currentCheckTask, startOthersTask).ConfigureAwait(false);
            if (currentCheckTask is { IsCompletedSuccessfully: true, Result: not null })
                return;

            // A slow or failed check starts the others next to it, as RpcPathManager does
            var checkTasks = new Dictionary<Task<RpcPathMeasurement?>, string> { [currentCheckTask] = current };
            foreach (var endpoint in selector.Candidates)
                if (!string.Equals(endpoint, current, StringComparison.OrdinalIgnoreCase))
                    checkTasks[Probe.Check(endpoint, cts.Token)] = endpoint;
            while (checkTasks.Count != 0) {
                var checkTask = await Task.WhenAny(checkTasks.Keys).ConfigureAwait(false);
                checkTasks.Remove(checkTask, out var endpoint);
                var measurement = await checkTask.ConfigureAwait(false);
                if (measurement is null)
                    continue;

                if (Use(selector, endpoint!))
                    Log.LogWarning("RPC endpoint {Current} is slow or failed its check, moved to {Endpoint}",
                        current, endpoint);
                return;
            }
        }
        finally {
            // The checks still running lost the race
            cts.Cancel();
        }

        throw StandardError.Unavailable("No RPC endpoint passed its check.");
    }

    private bool Use(RpcEndpointSelector selector, string endpoint)
    {
        if (string.Equals(endpoint, selector.Current, StringComparison.OrdinalIgnoreCase)
            || !selector.Use(endpoint))
            return false;

        var peer = RpcHub.GetClientPeer(RpcRef.Default);
        _ = peer.Disconnect();
        peer.ResetConnectionAttemptIndex();
        RpcHub.InternalServices.ClientPeerReconnectDelayer.CancelDelays();
        return true;
    }
}
