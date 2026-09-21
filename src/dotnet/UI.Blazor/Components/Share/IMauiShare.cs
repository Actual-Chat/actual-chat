namespace ActualChat.UI.Blazor.Components;

public interface IMauiShare
{
    public Task Share(ShareRequest request, IProgress<double>? progress = null);
    // Starts fetching the request's media, so the share sheet opens without a download behind it
    public Task Prefetch(ShareRequest request);
}
