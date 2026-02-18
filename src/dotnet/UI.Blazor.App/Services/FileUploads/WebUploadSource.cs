namespace ActualChat.UI.Blazor.App.Services;

public class WebUploadSource : IUploadSource
{
    public UploadSourceMetadata Metadata { get; }
    public IWebFileProviderInternal WebFileProviderInternal { get; }

    public WebUploadSource(
        UploadSourceMetadata metadata,
        IWebFileProviderInternal webFileProviderInternal)
    {
        Metadata = metadata;
        WebFileProviderInternal = webFileProviderInternal;
    }
}
