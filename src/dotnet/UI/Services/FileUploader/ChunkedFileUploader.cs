namespace ActualChat.UI.Services;

/// <summary>
/// Handles resumable file uploads with adaptive chunk sizing and retry logic.
/// </summary>
public sealed class ChunkedFileUploader(IServiceProvider services)
{
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.5d, 3);

    private IServiceProvider Services => services;
    private ChunkSizeSelectorRecommendation ChunkSizeSelectorRecommendation => field ??= Services.GetRequiredService<ChunkSizeSelectorRecommendation>();
    private Session Session => field ??= Services.Session();
    private ICommander Commander => field ??= Services.Commander();
    private ILogger Log => field ??= Services.LogFor(GetType());
    private IUploads Uploads => field ??= Services.GetRequiredService<IUploads>();

    public async Task UploadData(
        UploadId uploadId,
        Task<Stream> getStream,
        IProgress<double> progressTracker,
        CancellationToken ct)
    {
        var file = await getStream.ConfigureAwait(false);
        var retryIndex = 0;
        const int maxRetries = 3;
        var chunkSizeSelector = new ChunkSizeSelector(ChunkSizeSelectorRecommendation, Services.LogFor<ChunkSizeSelector>());
        await using (_ = file.ConfigureAwait(false)) {
            bool run = true;
            while (run) {
                run = false;
                try {
                    await UploadDataInternal(
                            uploadId,
                            file,
                            progressTracker,
                            chunkSizeSelector,
                            () => retryIndex = 0,
                            ct)
                        .ConfigureAwait(false);
                }
                catch (UploadTransientException e) when (retryIndex <= maxRetries) {
                    Log.LogWarning(e, "Upload transient failure. Retrying...");
                    chunkSizeSelector.AdaptOnUploadIssue(false);
                    retryIndex++;
                    await Task.Delay(RetryDelays.GetDelay(retryIndex), ct).ConfigureAwait(false);
                    run = true;
                }
                catch (OffsetConflictException e) when (retryIndex <= maxRetries) {
                    Log.LogWarning(e, "Offset conflict detected. Retrying...");
                    chunkSizeSelector.AdaptOnUploadIssue(false);
                    retryIndex++;
                    run = true;
                }
            }
            // Persist recommendation for following uploads within app lifetime
            chunkSizeSelector.UpdateRecommendation();
        }
    }

    private async Task UploadDataInternal(
        UploadId uploadId,
        Stream file,
        IProgress<double> progressTracker,
        ChunkSizeSelector chunkSizeSelector,
        Action onChunkUploadSucceeded,
        CancellationToken ct)
    {
        var offset = await Uploads.GetOffset(Session, uploadId, ct).ConfigureAwait(false);
        Log.LogDebug("Starting upload of {UploadId} at offset {Offset}", uploadId, offset);

        if (offset > 0) {
            if (file.CanSeek)
                file.Seek(offset, SeekOrigin.Begin);
            else
                throw StandardError.Internal("Cannot seek in non-seekable stream.");
        }

        // Report initial progress based on current offset
        if (file.Length > 0)
            progressTracker.Report(offset / (double)file.Length * 100);

        // Upload chunks
        while (offset < file.Length) {
            var remainingBytes = file.Length - offset;
            var targetChunkSize = chunkSizeSelector.GetChunkSize();
            var currentChunkSize = (int)Math.Min(targetChunkSize, remainingBytes);

            var chunkBuffer = new byte[currentChunkSize];
            var bytesRead = await file.ReadAsync(chunkBuffer.AsMemory(0, currentChunkSize), ct).ConfigureAwait(false);

            if (bytesRead < currentChunkSize)
                Log.LogWarning(
                    "Unexpected EOF while reading chunk {Offset}. "
                    + "Expected to read {ChunkSize} bytes, but read only {ReadBytes} bytes",
                    offset, currentChunkSize, bytesRead);
            if (bytesRead == 0)
                break;

            var timestamp = Stopwatch.GetTimestamp();
            var appendCmd = new Uploads_Append(Session, uploadId, offset, chunkBuffer);
            var newOffset = await Commander.Call(appendCmd, ct).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            offset += bytesRead;
            if (offset != newOffset)
                Log.LogWarning("Offset mismatch detected: {ExpectedOffset} != {NewOffset}", offset, newOffset);
            onChunkUploadSucceeded();
            chunkSizeSelector.AdaptChunkSizeOnSucceedUpload(elapsed);

            var uploadProgress = offset / (double)file.Length * 100;
            progressTracker.Report(uploadProgress);
        }
    }
}

/// <summary>
/// Stores the recommended chunk size multiplier across uploads within an app session.
/// </summary>
public sealed class ChunkSizeSelectorRecommendation
{
    public const int DefaultMultiplier = 8;

    public int Multiplier { get; set; } = DefaultMultiplier;
}

internal sealed class ChunkSizeSelector
{
    private const int MinChunkSize = 256 * 1024; // 256 KB
    private const int MaxChunkSizeMultiplier = 16; // 16 x 256 KB = 4 MB, see Constants.Uploads.MaxChunkSize
    private const int MaxChunkUploadDurationMs = 5000; // 5 seconds

    private readonly Queue<(int multiplier, long ms)> _lastStats = new();
    private readonly ChunkSizeSelectorRecommendation _recommendation;
    private readonly ILogger _log;
    private int _currentMultiplier;

    public ChunkSizeSelector(ChunkSizeSelectorRecommendation recommendation, ILogger log)
    {
        _log = log;
        _recommendation = recommendation;
        _currentMultiplier = recommendation.Multiplier;
    }

    public int GetChunkSize()
        => _currentMultiplier * MinChunkSize;

    public void AdaptChunkSizeOnSucceedUpload(TimeSpan duration)
    {
        var durationMs = (long)duration.TotalMilliseconds;
        // track stats (limit to last 5)
        _lastStats.Enqueue((_currentMultiplier, durationMs));
        while (_lastStats.Count > 5)
            _lastStats.Dequeue();

        var isSlow = durationMs > MaxChunkUploadDurationMs;
        if (isSlow) {
            if (_currentMultiplier == 1)
                return;
        }
        else {
            if (_currentMultiplier == MaxChunkSizeMultiplier)
                return;
        }

        double averagePerf;
        if (_lastStats.Count > 1) {
            const double minWeight = 0.2; // 20%
            var lastIndex = _lastStats.Count - 1;
            var step = 0;
            double totalPerf = 0;
            double totalWeights = 0;
            foreach (var stat in _lastStats.Reverse()) {
                var (mult, ms) = stat;
                var perf = ms <= 0 ? 0 : (double)mult / ms;
                var z = lastIndex == 0 ? 0 : (double)step / lastIndex;
                var weight = Math.Pow(minWeight, z);
                totalPerf += perf * weight;
                totalWeights += weight;
                step++;
            }
            averagePerf = totalWeights > 0 ? totalPerf / totalWeights : 0.5;
        }
        else {
            var (mult, ms) = _lastStats.Peek();
            averagePerf = ms <= 0 ? 0.5 : (double)mult / ms;
        }

        var averageMultiplier = averagePerf * MaxChunkUploadDurationMs;
        var newMultiplier = (int)Math.Round(averageMultiplier);
        newMultiplier = Math.Min(Math.Max(newMultiplier, 1), MaxChunkSizeMultiplier);
        _currentMultiplier = newMultiplier;
        _log.LogDebug("Adapted chunkSizeMultiplier={Multiplier}", _currentMultiplier);
    }

    public void AdaptOnUploadIssue(bool isConnectionIssue)
    {
        if (_currentMultiplier == 1)
            return;

        if (isConnectionIssue && _currentMultiplier > ChunkSizeSelectorRecommendation.DefaultMultiplier)
            _currentMultiplier = ChunkSizeSelectorRecommendation.DefaultMultiplier;
        else
            _currentMultiplier--;
        _log.LogDebug("Adapted chunkSizeMultiplier={Multiplier} on error", _currentMultiplier);
    }

    public void UpdateRecommendation()
    {
        _recommendation.Multiplier = _currentMultiplier;
        _log.LogDebug("Set recommendedChunkSizeMultiplier={Multiplier}", _currentMultiplier);
    }
}
