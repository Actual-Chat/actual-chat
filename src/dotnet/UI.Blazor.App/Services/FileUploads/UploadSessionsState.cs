using System.Collections.Immutable;

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

    [ComputeMethod]
    public virtual Task<ImmutableArray<string>> GetActiveSessionIds(CancellationToken cancellationToken)
    {
        var ids = _progresses
            .Where(kv => IsActive(kv.Value))
            .Select(kv => kv.Key)
            .ToImmutableArray();
        return Task.FromResult(ids);
    }

    public void SetProgress(string sessionId, UploadSessionProgress progress)
    {
        var wasActive = IsActive(_progresses.GetValueOrDefault(sessionId));
        _progresses[sessionId] = progress;
        var isActive = IsActive(progress);
        using (Invalidation.Begin()) {
            _ = GetProgress(sessionId, default);
            if (wasActive != isActive)
                _ = GetActiveSessionIds(default);
        }
    }

    public void Remove(string sessionId)
    {
        var wasActive = IsActive(_progresses.GetValueOrDefault(sessionId));
        _progresses.TryRemove(sessionId, out _);
        using (Invalidation.Begin()) {
            _ = GetProgress(sessionId, default);
            if (wasActive)
                _ = GetActiveSessionIds(default);
        }
    }

    // A failed session keeps its stage but is waiting for a restart, not uploading
    private static bool IsActive(UploadSessionProgress? progress)
        => progress is { IsFailed: false, Stage: UploadStage.Uploading or UploadStage.ServerProcessing };
}
