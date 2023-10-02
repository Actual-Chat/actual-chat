using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Android.App;
using Android.Content;

namespace ActualChat.App.Maui;

public class AndroidVisualMediaViewerFileDownloader : IVisualMediaViewerFileDownloader
{
    private readonly object _syncObject = new object();
    private readonly List<long> _pendingDownloads = new List<long>();
    private DownloadCompletedBroadcastReceiver? _downloadCompletedReceiver;
    private ILogger<AndroidVisualMediaViewerFileDownloader> Log { get; }
    private ToastUI ToastUI { get; }

    public AndroidVisualMediaViewerFileDownloader(ILogger<AndroidVisualMediaViewerFileDownloader> log, ToastUI toastUI)
    {
        ToastUI = toastUI;
        Log = log;
    }

    public Task Download(string sUri)
    {
        var uri = Android.Net.Uri.Parse(sUri);
        if (uri == null) {
            Log.LogWarning("Invalid uri provided '{Uri}'", sUri);
            return Task.CompletedTask;
        }

        EnsureDownloadCompletedReceiverRegistered();

        var appContext = Platform.AppContext;
        try {
            var request = new DownloadManager.Request(uri);
            request.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
            var dir = Android.OS.Environment.DirectoryPictures;
            var fileName = uri.PathSegments?.LastOrDefault() ?? "download";
            request.SetDestinationInExternalPublicDir(dir, Path.Combine("ActualChat", fileName));
            var dm = (DownloadManager)appContext.GetSystemService(Android.Content.Context.DownloadService)!;
            lock (_syncObject) {
                var downloadRef = dm.Enqueue(request);
                _pendingDownloads.Add(downloadRef);
            }
            return Task.CompletedTask;
        }
        catch(Exception e) {
            Log.LogError(e, "Failed to start file downloading");
            return Task.CompletedTask;
        }
    }

    private void EnsureDownloadCompletedReceiverRegistered()
    {
        if (_downloadCompletedReceiver != null)
            return;
        _downloadCompletedReceiver = new DownloadCompletedBroadcastReceiver(OnDownloadCompleted);
        Platform.AppContext.RegisterReceiver(_downloadCompletedReceiver, new IntentFilter(DownloadManager.ActionDownloadComplete));
    }

    private void OnDownloadCompleted(long downloadRef)
    {
        lock (_syncObject) {
            var removed = _pendingDownloads.Remove(downloadRef);
            if (!removed)
                downloadRef = -1;
        }
        if (downloadRef < 0)
            return;

        var dm = (DownloadManager)Platform.AppContext.GetSystemService(Android.Content.Context.DownloadService)!;
        var uri = dm.GetUriForDownloadedFile(downloadRef);
        if (uri == null)
            return;
        MainThread.BeginInvokeOnMainThread(() => {
            ToastUI.Show("1 file downloaded", ToastDismissDelay.Short);
        });
    }

    [BroadcastReceiver(Enabled = true, Exported = false, Label = "Download completion Broadcast Receiver")]
    private class DownloadCompletedBroadcastReceiver : BroadcastReceiver
    {
        private readonly Action<long>? _onDownloadCompleted;

        public DownloadCompletedBroadcastReceiver() {}

        public DownloadCompletedBroadcastReceiver(Action<long> onDownloadCompleted)
            => this._onDownloadCompleted = onDownloadCompleted;

        public override void OnReceive(Context? context, Intent? intent)
        {
            long id = intent!.GetLongExtra(DownloadManager.ExtraDownloadId, -1);
            if (id >= 0)
                _onDownloadCompleted?.Invoke(id);
        }
    }
}
