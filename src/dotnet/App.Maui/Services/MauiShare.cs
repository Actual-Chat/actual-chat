using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using DataTransfer = Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of share functionality using platform-specific share dialogs.
/// </summary>
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiShare))]
public sealed class MauiShare(IServiceProvider services) : IMauiShare
{
    private UrlMapper UrlMapper => field ??= services.UrlMapper();
    private MauiTempFileDownloader TempFileDownloader => field ??= new MauiTempFileDownloader(services);
    private ToastUI ToastUI => field ??= services.GetRequiredService<ToastUI>();
    private ILogger Log => field ??= services.LogFor(GetType());

    public Task Share(ShareRequest request)
    {
        if (request.HasMedia())
            return ShareMedia(request);

        var textAndLink = request.GetShareTextAndLink(UrlMapper);
        if (textAndLink.IsNullOrEmpty())
            return Task.CompletedTask;

        var link = request.GetShareLink(UrlMapper).NullIfEmpty();
        return DataTransfer.Share.Default.RequestAsync(new DataTransfer.ShareTextRequest {
            Title = request.Text,
            Text = textAndLink,
            Uri = link,
        });
    }

    // Private methods

    private async Task ShareMedia(ShareRequest request)
    {
        try {
            var files = new List<DataTransfer.ShareFile>();
            foreach (var media in request.Media) {
                // ToOrigin: HttpClient can't fetch the WebView-only content:// URL ContentUrl returns on Apple
                var url = UrlMapper.ToOrigin(UrlMapper.ContentUrl(media.Ref.BlobId));
                var filePath = await TempFileDownloader
                    .Download(url, media.GetFileName(), media.ContentType)
                    .ConfigureAwait(false);
                files.Add(new DataTransfer.ShareFile(filePath, media.ContentType));
            }

            var title = request.Text.NullIfEmpty() ?? files[0].FileName;
            // The share sheet is a UIViewController presentation on Apple platforms, so it must
            // happen on the main thread - off it, iOS drops it silently.
            await MainThread.InvokeOnMainThreadAsync(() => files.Count == 1
                ? DataTransfer.Share.Default.RequestAsync(new DataTransfer.ShareFileRequest {
                    Title = title,
                    File = files[0],
                })
                : DataTransfer.Share.Default.RequestAsync(new DataTransfer.ShareMultipleFilesRequest {
                    Title = title,
                    Files = files,
                })).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to share media externally");
            ToastUI.Show("Failed to share media", "icon-alert-circle", ToastDismissDelay.Short);
        }
    }
}
