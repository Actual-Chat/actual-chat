using ActualChat.Logging;
using ActualChat.Users;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.Services;

public class LogUI(UIHub hub) : UIWorkerBase<UIHub>(hub), IComputeService, ILogSink, INotifyInitialized
{
    private static readonly string OwnLogCategory = $"{typeof(LogUI).Namespace}.{nameof(LogUI)}";
    private static readonly TileStack<long> IdTileStack = Constants.TileStacks.Long5To80;
    private readonly Lock _lock = new();
    private readonly AsyncTaskMethodBuilder _whenReady = AsyncTaskMethodBuilderExt.New();
    private RingBuffer<LogEntry> _events = new(10_000);
    private long _entryIdGenerator = 1;
    private ComputedState<bool>? _isEnabled;

    private LogSinks LogSinks => Hub.LogSinks;

    public static ILogger? DiagLog { get; set; }
    public IState<bool> IsEnabled => _isEnabled!;
    public Task WhenReady => _whenReady.Task;

    void INotifyInitialized.Initialized()
    {
        _isEnabled = Hub.StateFactory.NewComputed(ct
            => LocalSettings.LocalAppSettings().Get(x => x.IsLogViewerEnabledOrDefault, ct));
        Hub.RegisterDisposable(_isEnabled);
        Hub.RegisterDisposable(() => LogSinks.Remove(this));
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
        if (categoryName.OrdinalStartsWith(OwnLogCategory)) {
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

            var idTiles = IdTileStack.FirstLayer.GetCoveringTiles(idRange).OrderBy(x => x.Start).ToList();
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
    protected virtual async Task<LogTile> GetTile(Tile<long> idTile, CancellationToken cancellationToken)
    {
        var smallerIdTiles = idTile.Smaller();
        if (smallerIdTiles.Length != 0) {
            var smallerTiles = await smallerIdTiles
                .Select(sidTile => GetTile(sidTile, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            return LogTile.From(smallerTiles);
        }

        lock (_lock) {
            if (_events.Count == 0)
                return LogTile.Empty;

            var intersectingRange = idTile.Range.IntersectWith(new(_events[0].Id, _events[^1].Id + 1));
            if (intersectingRange.IsEmptyOrNegative)
                return LogTile.Empty;

            _events.GetSpans(out var first, out var second);
            var tile = new LogTile(idTile.Range, [..GetSpan(first, intersectingRange), ..GetSpan(second, intersectingRange)]);
            return tile;
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

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var cAccount = await AccountUI.OwnAccount.Computed
            .When(x => !x.IsGuestOrNull(), cancellationToken)
            .ConfigureAwait(false);
        if (!cAccount.Value.IsAdmin)
            return;

        LogSinks.Add(this);
        _whenReady.TrySetResult();
    }

    private void InvalidateTiles(long id)
    {
        using (Invalidation.Begin()) {
            _ = GetIdRange(default);
            var smallestTile = IdTileStack.FirstLayer.GetTile(id);
            DiagLog?.LogDebug("Invalidating {Tile} for #{LogEntryId}", smallestTile, id);
            _ = GetTile(smallestTile, default);
            //foreach (var tile in IdTileStack.GetAllTiles(id)) {
            //    DiagLog?.LogDebug("Invalidating {Tile} for #{LogEntryId}", tile, id);
            //    _ = GetTile(tile, default);
            //}
        }
    }
}
