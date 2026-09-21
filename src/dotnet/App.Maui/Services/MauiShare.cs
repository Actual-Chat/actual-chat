using System.Collections.Concurrent;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using ActualLab.IO;
using DataTransfer = Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of share functionality using platform-specific share dialogs.
/// </summary>
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiShare))]
public sealed class MauiShare(IServiceProvider services) : IMauiShare
{
    private readonly ConcurrentDictionary<string, MediaDownload> _downloads = new(StringComparer.Ordinal);

    private UrlMapper UrlMapper => field ??= services.UrlMapper();
    private MauiTempFileDownloader TempFileDownloader => field ??= new MauiTempFileDownloader(services);
    private ToastUI ToastUI => field ??= services.GetRequiredService<ToastUI>();
    private ILogger Log => field ??= services.LogFor(GetType());

    public Task Share(ShareRequest request, IProgress<double>? progress = null)
    {
        if (request.HasMedia())
            return ShareMedia(request, progress);

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

    public Task Prefetch(ShareRequest request)
    {
        // A failure here is left to Share: it awaits the same download and reports it
        foreach (var media in request.Media)
            _ = GetDownload(media).WhenCompleted.SuppressExceptions();
        return Task.CompletedTask;
    }

    // Private methods

    private async Task ShareMedia(ShareRequest request, IProgress<double>? progress)
    {
        try {
            var media = request.Media;
            var files = new List<DataTransfer.ShareFile>(media.Count);
            for (var i = 0; i < media.Count; i++) {
                var index = i;
                var fileProgress = progress == null
                    ? null
                    : new InlineProgress(x => progress.Report((100d * index + x) / media.Count));
                var filePath = await GetFile(media[index], fileProgress).ConfigureAwait(false);
                files.Add(new DataTransfer.ShareFile(filePath, media[index].ContentType));
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
        finally {
            // The paths are cached only to bridge the modal's prefetch to the share that follows
            _downloads.Clear();
        }
    }

    private async Task<FilePath> GetFile(SharedMedia media, IProgress<double>? progress)
    {
        var download = GetDownload(media);
        download.Subscribe(progress);
        var filePath = await download.WhenCompleted.ConfigureAwait(false);
        if (File.Exists(filePath))
            return filePath;

        // The sweep in MauiTempFileDownloader removes copies an old prefetch left behind
        _downloads.TryRemove(download.Url, out _);
        download = GetDownload(media);
        download.Subscribe(progress);
        return await download.WhenCompleted.ConfigureAwait(false);
    }

    private MediaDownload GetDownload(SharedMedia media)
    {
        // ToOrigin: HttpClient can't fetch the WebView-only content:// URL ContentUrl returns on Apple
        var url = UrlMapper.ToOrigin(UrlMapper.ContentUrl(media.Ref.BlobId));
        return _downloads.GetOrAdd(url, static (x, state) => new MediaDownload(state.Self, x, state.Media),
            (Self: this, Media: media));
    }

    // Nested types

    private sealed class MediaDownload
    {
        private IProgress<double>? _subscriber;
        private double _percent;

        public string Url { get; }
        public Task<FilePath> WhenCompleted { get; }

        public MediaDownload(MauiShare owner, string url, SharedMedia media)
        {
            Url = url;
            WhenCompleted = Download(owner, media);
        }

        public void Subscribe(IProgress<double>? progress)
        {
            // Publication: the download reports from a pool thread, this runs on the dispatcher
            Volatile.Write(ref _subscriber, progress);
            progress?.Report(Volatile.Read(ref _percent)); // Whatever a prefetch got through already
        }

        // Private methods

        private async Task<FilePath> Download(MauiShare owner, SharedMedia media)
        {
            try {
                return await owner.TempFileDownloader
                    .Download(Url, media.GetFileName(), media.ContentType, new InlineProgress(OnProgress))
                    .ConfigureAwait(false);
            }
            catch {
                owner._downloads.TryRemove(Url, out _); // A failed download must not be reused
                throw;
            }
        }

        private void OnProgress(double percent)
        {
            Volatile.Write(ref _percent, percent);
            Volatile.Read(ref _subscriber)?.Report(percent);
        }
    }

    // Progress<T> hops to the context it was created on; the subscriber does its own dispatch
    private sealed class InlineProgress(Action<double> handler) : IProgress<double>
    {
        public void Report(double value)
            => handler.Invoke(value);
    }
}
