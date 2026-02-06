namespace ActualChat.Media;

/// <summary>
/// Extension methods for <see cref="IGrabStatusesBackend"/>.
/// </summary>
public static class GrabStatusesBackendExt
{
    public static Task<GrabStatus?> GetByUrl(this IGrabStatusesBackend backend, string url, CancellationToken cancellationToken)
        => backend.Get(GrabStatus.ComposeId(url), cancellationToken);
}
