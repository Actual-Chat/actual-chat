namespace ActualChat.UI.Blazor.App.Services;

public class UploadSessionsState(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<string, UploadSessionProgress> _progresses = new();

    [ComputeMethod]
    public virtual Task<UploadSessionProgress> GetProgress(string sessionId, CancellationToken cancellationToken)
    {
        var progress = _progresses.GetValueOrDefault(sessionId) ?? UploadSessionProgress.New;
        return Task.FromResult(progress);
    }

    public void SetProgress(string sessionId, UploadSessionProgress progress)
    {
        _progresses[sessionId] = progress;
        using (Invalidation.Begin())
            _ = GetProgress(sessionId, default);
    }

    public void Remove(string sessionId)
    {
        _progresses.TryRemove(sessionId, out _);
        using (Invalidation.Begin())
            _ = GetProgress(sessionId, default);
    }
}
