using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.OS;
using Activity = Android.App.Activity;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public class IncomingShareHandler
{
    private ILogger Log { get; set; } = NullLogger.Instance;

    public void OnPostCreate(Activity activity, Bundle? savedInstanceState)
    {
        Log = AppServices.LogFor(GetType());
        TryHandleSend(activity.Intent);
    }

    public void OnNewIntent(Activity activity, Intent? intent)
        => TryHandleSend(intent);

    private void TryHandleSend(Intent? intent)
    {
        if (intent == null)
            return;
        var action = intent.Action;
        if (!OrdinalEquals(action, Intent.ActionSend) &&
            !OrdinalEquals(action, Intent.ActionSendMultiple))
            return;

        var mimeType = intent.Type ?? "";
        if (OrdinalEquals(action, Intent.ActionSend)) {
            if (OrdinalEquals(mimeType, System.Net.Mime.MediaTypeNames.Text.Plain)) {
                _ = HandleTextSend(mimeType, intent.GetStringExtra(Intent.ExtraText));
            }
            else if (mimeType.OrdinalStartsWith("image/")) {
                object? stream;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) {
                    var uriTypeClass = Java.Lang.Class.FromType(typeof(Android.Net.Uri));
                    stream = intent.GetParcelableExtra(Intent.ExtraStream, uriTypeClass);
                }
                else
                    stream = intent.GetParcelableExtra(Intent.ExtraStream);
                if (stream is Android.Net.Uri uri)
                    _ = HandleFilesSend(mimeType, new []{ uri });
                else
                    Log.LogWarning("Unsupported stream type: '{StreamType}'", stream?.ToString() ?? "<null>");
            }
            else
                Log.LogWarning("Unsupported send mime type: '{MimiType}'", mimeType);
        }
        else {
            if (mimeType.OrdinalStartsWith("image/")) {
                IList? streams;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) {
                    var uriTypeClass = Java.Lang.Class.FromType(typeof(Android.Net.Uri));
                    streams = intent.GetParcelableArrayListExtra(Intent.ExtraStream, uriTypeClass);
                }
                else
                    streams = intent.GetParcelableArrayListExtra(Intent.ExtraStream);
                if (streams == null)
                    Log.LogWarning("No file streams provided");
                else {
                    var uris = new List<Android.Net.Uri>();
                    foreach (var stream in streams) {
                        if (stream is Android.Net.Uri uri)
                            uris.Add(uri);
                        else
                            Log.LogWarning("Unsupported stream type: '{StreamType}'", stream?.ToString() ?? "<null>");
                    }
                    if (uris.Count > 0)
                        _ = HandleFilesSend(mimeType, uris);
                    else
                        Log.LogWarning("No supported image files provided");
                }
            }
            else {
                Log.LogWarning("Unsupported send mime type: '{MimiType}'", mimeType);
            }
        }
    }

    private async Task HandleTextSend(string mimeType, string? text)
    {
        if (text.IsNullOrEmpty()) {
            Log.LogWarning("No text to send");
            return;
        }
        Log.LogInformation("About to send text: '{Text}'", text);
        await InvokeAsync(services => {
            var incomingShareUI = services.GetRequiredService<IncomingShareUI>();
            incomingShareUI.ShareText(text ?? "");
        }).ConfigureAwait(false);
    }

    private async Task HandleFilesSend(string mimeType, ICollection<Android.Net.Uri> uris)
    {
        if (uris.Count == 0) {
            Log.LogWarning("No files to send");
            return;
        }
        Log.LogInformation("About to send {Count} files of type '{MimeType}'", uris.Count, mimeType);
        await InvokeAsync(services => {
            var incomingShareUI = services.GetRequiredService<IncomingShareUI>();
            var fileInfos = uris.Select(c => new AndroidFileInfo(c)).ToArray<IncomingShareFile>();
            incomingShareUI.ShareFiles(mimeType, fileInfos);
        }).ConfigureAwait(false);
    }

    private async Task InvokeAsync(Action<IServiceProvider> workItem)
    {
        var services = await ScopedServicesTask.ConfigureAwait(false);
        var loadingUI = services.GetRequiredService<LoadingUI>();
        await loadingUI.WhenRendered.ConfigureAwait(false);
        var historyUI = services.GetRequiredService<History>();
        await historyUI.Dispatcher.InvokeAsync(() => workItem(services)).ConfigureAwait(false);
    }
}

public class AndroidFileInfo : IncomingShareFile
{
    public AndroidFileInfo(Uri uri)
        => Uri = uri;

    public Android.Net.Uri Uri { get; }
    public override string Url => Uri.ToString();
}
