namespace ActualChat.UI.Blazor.Services;

public class ReplicaCacheStoragePerfMonitor : IReplicaCacheStorage
{
    private readonly PerfMetrics _getPerfMetrics = new ();
    private readonly PerfMetrics _setPerfMetrics = new ();
    private ILogger Log  { get; }
    private IReplicaCacheStorage Storage { get; }

    public static IReplicaCacheStorage EnablePerfMonitor(bool isEnabled, IReplicaCacheStorage storage, ILogger log)
        => isEnabled && log.IsEnabled(LogLevel.Debug) ? new ReplicaCacheStoragePerfMonitor(storage, log) : storage;

    public ReplicaCacheStoragePerfMonitor(IReplicaCacheStorage storage, ILogger log)
    {
        Log = log;
        Storage = storage;
    }

    public async Task<string?> TryGetValue(string key)
    {
        var region = _getPerfMetrics.Start();
        var sValue = await Storage.TryGetValue(key).ConfigureAwait(false);
        var regionResult = region.Close();
        Log.LogDebug("Get from store {Duration}/{TotalElapsed}ms ({Number}) -> ({Key})",
            regionResult.Duration, region.StartedAt + regionResult.Duration, regionResult.CompletionIndex, key);
        return sValue;
    }

    public async Task SetValue(string key, string value)
    {
        var region = _setPerfMetrics.Start();
        await Storage.SetValue(key, value).ConfigureAwait(false);
        var regionResult = region.Close();
        Log.LogDebug("Save to store {Duration}/{TotalElapsed}ms ({Number}) -> ({Key})",
            regionResult.Duration, region.StartedAt + regionResult.Duration, regionResult.CompletionIndex, key);
    }

    public Task Clear()
        => Storage.Clear();

    private class PerfMetrics
    {
        private readonly object _lock = new ();
        private readonly Stopwatch _totalElapsed;
        private long _completedRegionsNumber;
        private int _activeRegions;

        public Region Start()
        {
            var sw = Stopwatch.StartNew();
            long startedAt;
            lock (_lock) {
                startedAt = _totalElapsed.ElapsedMilliseconds;
                if (_activeRegions == 0)
                    _totalElapsed.Start();
                _activeRegions++;
            }
            return new Region {Owner = this, Stopwatch = sw, StartedAt = startedAt};
        }

        private RegionResult Close(Region region)
        {
            long totalNumber;
            lock (_lock) {
                _activeRegions--;
                if (_activeRegions == 0)
                    _totalElapsed.Stop();
                _completedRegionsNumber++;
                totalNumber = _completedRegionsNumber;
            }
            region.Stopwatch.Stop();
            return new RegionResult {
                Duration = region.Stopwatch.ElapsedMilliseconds,
                CompletionIndex = totalNumber,
            };
        }

        public PerfMetrics()
            => _totalElapsed = new Stopwatch();

        [StructLayout(LayoutKind.Auto)]
        public readonly struct RegionResult
        {
            public long Duration { get; init; }
            public long CompletionIndex { get; init; }
        }

        public readonly struct Region
        {
            public PerfMetrics Owner { get; init; }
            public Stopwatch Stopwatch { get; init; }
            public long StartedAt { get; init; }

            public RegionResult Close()
                => Owner.Close(this);
        }
    }
}
