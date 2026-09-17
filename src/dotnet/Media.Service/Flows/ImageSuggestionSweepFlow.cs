using ActualChat.Flows;
using ActualChat.Media.Module;

namespace ActualChat.Media.Flows;

// Collects suggestions nobody accepted or dismissed. Each holds a ~50KB blob that nothing else
// references, so leaving them is a slow leak rather than a correctness problem - hence a periodic
// sweep rather than a transaction-time delete.

[Flow(DelayQuanta = 60)]
[DataContract, MessagePackObject(true)]
public sealed partial class ImageSuggestionSweepFlow : PeriodicFlow, IMasterFlow
{
    private const int BatchSize = 100;

    private MediaSettings Settings => field ??= Services.GetRequiredService<MediaSettings>();
    private IImageSuggestionsBackend Backend => field ??= Services.GetRequiredService<IImageSuggestionsBackend>();
    private ICommander Commander => field ??= Services.Commander();

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var keys = await ListStale(cancellationToken).ConfigureAwait(false);
        return keys.Count == 0
            ? new FlowReadiness("No stale image suggestions", Settings.ImageSuggestionSweepInterval)
            : FlowReadiness.Ready;
    }

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        var keys = await ListStale(cancellationToken).ConfigureAwait(false);
        if (keys.Count == 0)
            return Hub.SystemNow + Settings.ImageSuggestionSweepInterval;

        Console.Log($"Sweeping {keys.Count} stale image suggestions");
        foreach (var key in keys) {
            var command = new ImageSuggestionsBackend_Remove(key, true);
            await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        }

        // A full batch means there is probably more waiting, so come straight back for it
        return keys.Count == BatchSize
            ? Hub.SystemNow
            : Hub.SystemNow + Settings.ImageSuggestionSweepInterval;
    }

    // Private methods

    private Task<ApiArray<string>> ListStale(CancellationToken cancellationToken)
    {
        var maxCreatedAt = Hub.SystemNow - Settings.ImageSuggestionLifespan;
        return Backend.ListStale(maxCreatedAt, BatchSize, cancellationToken);
    }
}
