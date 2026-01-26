namespace ActualChat.UI.Blazor.App.Services;

public class UploadSessionsState(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<string, MediaId> _reservedMediaIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UploadSessionProgress> _progresses = new(StringComparer.Ordinal);

    [ComputeMethod]
    public virtual Task<MediaId?> GetReservedMediaId(string sessionId, CancellationToken cancellationToken)
    {
        var mediaId = _reservedMediaIds.TryGetValue(sessionId, out var id) ? (MediaId?)id : null;
        return Task.FromResult(mediaId);
    }

    [ComputeMethod]
    public virtual Task<UploadSessionProgress> GetProgress(string sessionId, CancellationToken cancellationToken)
    {
        var progress = _progresses.GetValueOrDefault(sessionId) ?? UploadSessionProgress.New;
        return Task.FromResult(progress);
    }

    public void SetReservedMediaId(string sessionId, MediaId mediaId)
    {
        _reservedMediaIds[sessionId] = mediaId;
        using (Invalidation.Begin())
            _ = GetReservedMediaId(sessionId, default);
    }

    public void SetProgress(string sessionId, UploadSessionProgress progress)
    {
        _progresses[sessionId] = progress;
        using (Invalidation.Begin())
            _ = GetProgress(sessionId, default);
    }

    public void Remove(string sessionId)
    {
        _reservedMediaIds.TryRemove(sessionId, out _);
        _progresses.TryRemove(sessionId, out _);
        using (Invalidation.Begin()) {
            _ = GetReservedMediaId(sessionId, default);
            _ = GetProgress(sessionId, default);
        }
    }
}
