using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Android.App;
using Android.Content;

namespace ActualChat.App.Maui;

public class AndroidMediaSaver(IServiceProvider services)
    : IMediaSaver
{
    private readonly object _lock = new();
    private ToastUI? _toastUI;
    private ILogger? _log;
    private readonly List<long> _pendingDownloads = new ();
    private DownloadCompletedBroadcastReceiver? _downloadCompletedReceiver;

    private ToastUI ToastUI => _toastUI ??= services.GetRequiredService<ToastUI>();
    private ILogger Log => _log ??= services.LogFor(GetType());

    public Task Save(string sUri, string contentType)
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
            lock (_lock) {
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
        Platform.AppContext.RegisterReceiver(
            _downloadCompletedReceiver,
            new IntentFilter(DownloadManager.ActionDownloadComplete));
    }

    private void OnDownloadCompleted(long downloadRef)
    {
        lock (_lock) {
            var removed = _pendingDownloads.Remove(downloadRef);
            if (!removed)
                downloadRef = -1;
        }
        if (downloadRef < 0)
            return;

        var dm = Platform.AppContext.GetSystemService(Context.DownloadService) as DownloadManager;
        if (dm.IsNull())
            return;

        var uri = dm.GetUriForDownloadedFile(downloadRef);
        if (uri == null)
            return;

        MainThread.BeginInvokeOnMainThread(() => {
            ToastUI.Show("1 file downloaded", "icon-checkmark-circle-2", ToastDismissDelay.Short);
        });
    }

    [BroadcastReceiver(Enabled = true, Exported = false, Label = "Download completion Broadcast Receiver")]
    private class DownloadCompletedBroadcastReceiver(Action<long>? onDownloadCompleted) : BroadcastReceiver
    {
        public DownloadCompletedBroadcastReceiver() : this(null)
        { }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent.IsNull())
                return;

            var id = intent.GetLongExtra(DownloadManager.ExtraDownloadId, -1);
            if (id >= 0)
                onDownloadCompleted?.Invoke(id);
        }
    }
}
