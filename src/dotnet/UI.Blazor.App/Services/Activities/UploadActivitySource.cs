using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class UploadActivitySource : IActivitySource, IDisposable, IHasDisposeStatus
{
    private const double UploadUpdatePeriod = 1;
    private readonly ComputedState<UploadActivity?> _upload;
    private bool _isDisposed;
    private AppUIHub Hub { get; }
    public bool IsDisposed => _isDisposed;
    public UploadActivitySource(AppUIHub hub)
    {
        Hub = hub;
        _upload = hub.StateFactory.NewComputed(
            new ComputedState<UploadActivity?>.Options() {
                // Byte counts move with every uploaded chunk, and each distinct value restarts
                // the Android foreground service and re-posts the iOS notification.
                UpdateDelayer = FixedDelayer.Get(UploadUpdatePeriod),
                TryComputeSynchronously = false,
                Category = StateCategories.Get(GetType(), nameof(GetActivity)),
            },
            ComputeUpload);
    }

    public void Dispose()
    {
        _isDisposed = true;
        _upload.Dispose();
    }

    [ComputeMethod]
    public virtual async Task<ActivityInfo?> GetActivity(CancellationToken cancellationToken)
        => await _upload.Use(cancellationToken).ConfigureAwait(false);

    // Private methods

    private async Task<UploadActivity?> ComputeUpload(CancellationToken cancellationToken)
    {
        var activeIds = await Hub.UploadSessionsState
            .GetActiveSessionIds(cancellationToken).ConfigureAwait(false);
        if (activeIds.IsDefaultOrEmpty)
            return null;

        var items = ImmutableList.CreateBuilder<UploadActivityItem>();
        long bytesUploaded = 0;
        long totalBytes = 0;
        foreach (var id in activeIds) {
            var session = await Hub.UploadSessions.TryGetSession(id).ConfigureAwait(false);
            if (session is null)
                continue;

            var progress = await Hub.UploadSessionsState
                .GetProgress(id, cancellationToken).ConfigureAwait(false);
            var length = session.FileProvider.Metadata.Length;
            var itemUploaded = (long)(length * progress.UploadedFraction);
            items.Add(new UploadActivityItem(id, session.FileName, itemUploaded, length));
            bytesUploaded += itemUploaded;
            totalBytes += length;
        }
        if (items.Count == 0)
            return null;

        return new UploadActivity(items.Count, bytesUploaded, totalBytes, items.ToImmutable());
    }
}
