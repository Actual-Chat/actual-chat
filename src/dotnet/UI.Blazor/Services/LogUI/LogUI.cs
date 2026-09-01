using ActualChat.Logging;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Captures application logs in a ring buffer for display in the in-app log viewer.
/// </summary>
public class LogUI(UIHub hub) : UIWorkerBase<UIHub>(hub), IComputeService, ILogSink, INotifyInitialized
{
    private static readonly string OwnLogCategory = $"{typeof(LogUI).Namespace}.{nameof(LogUI)}";
    private static readonly TileLayer<long> IdTileLayer = Constants.TileLayers.Long5;
    private readonly Lock _lock = new();
    private readonly AsyncTaskMethodBuilder _whenReady = AsyncTaskMethodBuilderExt.New();
    private RingBuffer<LogEntry> _events = new(10_000);
    private long _entryIdGenerator = 1;
    private ComputedState<bool>? _isEnabled;

    private TailLoggerSinkSet TailLoggerSinks => field ??= Hub.Services.GetRequiredService<TailLoggerSinkSet>();

    public static ILogger? DiagLog { get; set; }
    public IState<bool> IsEnabled => _isEnabled!;
    public Task WhenReady => _whenReady.Task;
    // Local logs are captured (and thus viewable) only off the server — SSB must not stream server logs.
    public bool CanCapture => !HostInfo.HostKind.IsServer() || HostInfo.IsTested;

    void INotifyInitialized.Initialized()
    {
        _isEnabled = Hub.StateFactory.NewComputed(
            ct => LocalSettings.LocalAppSettings().Get(x => x.IsLogViewerEnabledOrDefault, ct));
        Hub.RegisterDisposable(_isEnabled);
        Hub.RegisterDisposable(() => _whenReady.TrySetException(new ObjectDisposedException("LogUI is disposed")));
        this.Start();
    }

    void ILogSink.Log(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        if (!IsEnabled.ValueOrDefault) {
            DiagLog?.LogDebug("Log: Is disabled, skipped log entry: {LogLevel} {Category}: {Message}\n{Exception}", logLevel, categoryName, message, exception);
            return;
        }

        // Skip own logs
        if (categoryName.StartsWith(OwnLogCategory)) {
            DiagLog?.LogDebug("Log: Is own log, skipped log entry: {LogLevel} {Category}: {Message}\n{Exception}", logLevel, categoryName, message, exception);
            return;
        }

        DiagLog?.LogTrace("Log: {LogLevel} {Category}: {Message}\n{Exception}", logLevel, categoryName, message, exception);
        try {
            var addedItem = Push();
            InvalidateTiles(addedItem.Id);
        }
        catch (Exception e) {
            DiagLog?.LogError(e,
                "Log: Failed to push log entry: {LogLevel} {Category}: {Message}\n{Exception}",
                logLevel,
                categoryName,
                message,
                exception);
        }
        return;

        LogEntry Push()
        {
            lock (_lock) {
                var item = new LogEntry(_entryIdGenerator++, categoryName, logLevel, eventId, message, exception, Clocks.SystemClock.Now);
                _events.PushTailAndMoveHeadIfFull(item);
                return item;
            }
        }
    }

    [ComputeMethod(AutoInvalidationDelay = 10 * 60 * 1000)]
    public virtual async Task<IReadOnlyList<LogTile>> GetTiles(
        Range<long> idRange,
        CancellationToken cancellationToken)
    {
        try {
            idRange = idRange.Clamp(new(0, long.MaxValue));
            if (idRange.IsEmptyOrNegative)
                return [];

            var idTiles = IdTileLayer.GetCoveringTiles(idRange).OrderBy(x => x.Start).ToList();
            var tiles = await idTiles.Select(x => GetTile(x, cancellationToken)).Collect(cancellationToken).ConfigureAwait(false);
            tiles = [..tiles.Where(x => !x.IsEmpty)];
            DiagLog?.LogInformation("GetTiles: idRange={IdRange}, idTiles={IdTiles}, tilesCount={TilesCount}", idRange, idTiles, tiles.Length);
            return tiles;
        }
        catch (Exception e) {
            DiagLog?.LogError(e, "GetTiles: Failed to get logs for range {IdRange}", idRange);
            return [];
        }
    }

    [ComputeMethod]
    public virtual Task<Range<long>> GetIdRange(CancellationToken cancellationToken)
    {
        try {
            lock (_lock) {
                Range<long> idRange = _events.Count == 0 ? default : new(_events[0].Id, _events[^1].Id + 1);
                DiagLog?.LogDebug("GetIdRange: {Count} events in a buffer [{FirstId}..{LastId})", _events.Count, idRange.Start, idRange.End);
                return Task.FromResult(idRange);
            }
        }
        catch (Exception e) {
            DiagLog?.LogError(e, "GetIdRange: Failed to get id range");
            return Task.FromResult<Range<long>>(default);
        }
    }

    [ComputeMethod(AutoInvalidationDelay = 10 * 60 * 1000)]
    protected virtual Task<LogTile> GetTile(Tile<long> idTile, CancellationToken cancellationToken)
    {
        lock (_lock) {
            if (_events.Count == 0)
                return Task.FromResult(LogTile.Empty);

            var intersectingRange = idTile.Range.IntersectWith(new(_events[0].Id, _events[^1].Id + 1));
            if (intersectingRange.IsEmptyOrNegative)
                return Task.FromResult(LogTile.Empty);

            _events.GetSpans(out var first, out var second);
            var tile = new LogTile(idTile.Range, [..GetSpan(first, intersectingRange), ..GetSpan(second, intersectingRange)]);
            return Task.FromResult(tile);
        }

        static ReadOnlySpan<LogEntry> GetSpan(ReadOnlySpan<LogEntry> source, Range<long> intersectingRange)
        {
            if (source.IsEmpty)
                return source;

            var iStart = (int)(intersectingRange.Start - source[0].Id);
            var iEnd = (int)(intersectingRange.End - intersectingRange.Start + iStart).Clamp(0, source.Length);
            return source[iStart..iEnd];
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        if (CanCapture) { // SECURITY: No log streaming from SSB!
            // Capture the resolved sink set while the DI scope is alive and pair Remove with Add, so
            // teardown never re-resolves TailLoggerSinkSet from an already-disposed scope (which threw
            // ObjectDisposedException on the server, where CanCapture is false and Add never ran).
            var tailLoggerSinks = TailLoggerSinks;
            tailLoggerSinks.Add(this);
            Hub.RegisterDisposable(() => tailLoggerSinks.Remove(this));
        }
        _whenReady.TrySetResult();
        return Task.CompletedTask;
    }

    private void InvalidateTiles(long id)
    {
        using (Invalidation.Begin()) {
            _ = GetIdRange(default);
            var idTile = IdTileLayer.GetTile(id);
            DiagLog?.LogDebug("Invalidating {Tile} for #{LogEntryId}", idTile, id);
            _ = GetTile(idTile, default);
        }
    }
}
