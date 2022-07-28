using Stl.OS;

namespace ActualChat.Kvass;

public class KvassForBackend : IKvass, IDisposable
{
    private readonly Action<string[]> _onChanged;

    public IKvassBackend Backend { get; }
    public BatchProcessor<string, string?> GetProcessor { get; }
    public BatchProcessor<(string Key, string? Value), Unit> SetProcessor { get; }

    public KvassForBackend(IKvassBackend backend)
    {
        Backend = backend;
        Backend.Changed += _onChanged = OnChanged;
        GetProcessor = new BatchProcessor<string, string?>() {
            MaxBatchSize = 16,
            ConcurrencyLevel = Math.Min(HardwareInfo.ProcessorCount, 4),
            BatchingDelayTaskFactory = cancellationToken => Task.Delay(1, cancellationToken),
            Implementation = (batch, cancellationToken) => ProcessGetBatch(batch, cancellationToken),
        };
        SetProcessor = new BatchProcessor<(string Key, string? Value), Unit>() {
            MaxBatchSize = 16,
            ConcurrencyLevel = Math.Min(HardwareInfo.ProcessorCount, 4),
            BatchingDelayTaskFactory = cancellationToken => Task.Delay(1, cancellationToken),
            Implementation = (batch, cancellationToken) => ProcessSetBatch(batch, cancellationToken),
        };
    }

    public void Dispose()
        => Backend.Changed -= _onChanged;

    [ComputeMethod]
    public virtual ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
        => GetProcessor.Process(key, cancellationToken).ToValueTask();

    public async ValueTask Set(string key, string? value, CancellationToken cancellationToken = default)
        => await SetProcessor.Process((key, value), cancellationToken).ConfigureAwait(false);

    // Private methods

    private void OnChanged(string[] keys)
    {
        using (Computed.Invalidate()) {
            foreach (var key in keys)
                _ = Get(key);
        }
    }

    private async Task ProcessGetBatch(List<BatchItem<string, string?>> batch, CancellationToken cancellationToken)
    {
        var results = await Backend
            .GetMany(batch.Select(i => i.Input).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        for (var i = 0; i < batch.Count; i++) {
            var batchItem = batch[i];
            batchItem.SetResult(results[i], cancellationToken);
        }
    }

    private async Task ProcessSetBatch(List<BatchItem<(string Key, string? Value), Unit>> batch, CancellationToken cancellationToken)
    {
        await Backend
            .SetMany(batch.Select(i => i.Input).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        foreach (var batchItem in batch)
            batchItem.SetResult(default(Unit), cancellationToken);
    }
}
