using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

partial class UpgradeUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return AsyncChain.From(MonitorClientCompatibility)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private async Task MonitorClientCompatibility(CancellationToken cancellationToken)
    {
        Log.LogInformation(nameof(MonitorClientCompatibility));
        var clientVersion = ClientVersion;
        if (clientVersion.IsNullOrEmpty())
            return;

        var cClientCompatibility0 = await Computed
            .Capture(() => SystemProperties.CheckClientCompatibility(clientVersion, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cClientCompatibility0.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
        await foreach (var cClientCompatibility in changes.ConfigureAwait(false)) {
            var (newCompatibility, error) = cClientCompatibility;
            if (error != null)
                continue;

            Log.LogInformation("Got new client compatibility for version '{ClientVersion}' is {ClientCompatibility}", clientVersion, newCompatibility);
            var newStoredValue = new LocalClientCompatibility {
                ClientVersion = clientVersion,
                ClientCompatibility = newCompatibility,
            };
            if (newStoredValue != _storedState.Value)
                _storedState.Value = newStoredValue;

            // Reloads WebPage
            if (newCompatibility is SystemProperties_ClientCompatibility.Incompatible && HostInfo.HostKind.IsServerOrWasmApp())
                _ = BackgroundTask.Run(DispatchReloadUI, CancellationToken.None);
        }
    }

    private async Task DispatchReloadUI()
    {
        await Task.Delay(5000).ConfigureAwait(false);
        await Hub.Dispatcher.InvokeAsync(
            () => Hub.ReloadUI.Reload()
            ).ConfigureAwait(false);
    }
}
