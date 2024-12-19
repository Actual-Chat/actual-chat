namespace ActualChat.Media;

public static class GrabStatusesBackendExt
{
    public static Task<GrabStatus?> GetByUrl(
        this IGrabStatusesBackend grabStatusesBackend,
        string url,
        CancellationToken cancellationToken)
        => grabStatusesBackend.Get(GrabStatus.ComposeId(url), cancellationToken);
}
