namespace ActualChat.UI.Blazor.App.Services;

public class UploadSessionsState(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<string, MediaId> _reservedMediaIds = new(StringComparer.Ordinal);

    public void SetReservedMediaId(string uploadSessionId, MediaId mediaId)
    {
        _reservedMediaIds[uploadSessionId] = mediaId;
        using (Invalidation.Begin())
            _ = GetReservedMediaId(uploadSessionId, default);
    }

    public void Remove(string uploadSessionId)
    {
        _reservedMediaIds.TryRemove(uploadSessionId, out _);
        using (Invalidation.Begin())
            _ = GetReservedMediaId(uploadSessionId, default);
    }

    [ComputeMethod]
    public virtual Task<MediaId?> GetReservedMediaId(string uploadSessionId, CancellationToken cancellationToken)
    {
        var mediaId = _reservedMediaIds.TryGetValue(uploadSessionId, out var id) ? (MediaId?)id : null;
        return Task.FromResult(mediaId);
    }
}
