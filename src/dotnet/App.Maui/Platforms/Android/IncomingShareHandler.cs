using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.OS;
using Java.Lang;
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
        var hasExtraStream = intent.Extras?.ContainsKey(Intent.ExtraStream) ?? false;
        if (OrdinalEquals(action, Intent.ActionSend)) {
            if (OrdinalEquals(mimeType, System.Net.Mime.MediaTypeNames.Text.Plain))
                _ = HandlePlainTextSend(intent.GetStringExtra(Intent.ExtraText));
            else if (hasExtraStream) {
                var stream = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
                    ? intent.GetParcelableExtra(Intent.ExtraStream, Class.FromType(typeof(Uri)))
 #pragma warning disable CA1422
                    : intent.GetParcelableExtra(Intent.ExtraStream);
 #pragma warning restore CA1422
                if (stream is Uri uri)
                    _ = HandleFilesSend(mimeType, new[] { uri });
                else
                    Log.LogWarning("Unsupported stream type: '{StreamType}'", stream?.ToString() ?? "<null>");
            }
            else
                Log.LogWarning("Unsupported send mime type: '{MimiType}'", mimeType);
        }
        else {
            if (hasExtraStream) {
                var streams = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
                    ? intent.GetParcelableArrayListExtra(Intent.ExtraStream, Class.FromType(typeof(Uri)))
 #pragma warning disable CA1422
                    : intent.GetParcelableArrayListExtra(Intent.ExtraStream);
 #pragma warning restore CA1422
                if (streams == null)
                    Log.LogWarning("No file streams provided");
                else {
                    var uris = streams.OfType<Uri>().ToArray();
                    if (uris.Length > 0)
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

    private async Task HandlePlainTextSend(string? text)
    {
        if (text.IsNullOrEmpty()) {
            Log.LogWarning("No text to send");
            return;
        }
        Log.LogInformation("About to send text: '{Text}'", text);
        await InvokeAsync(services => {
            var incomingShareUI = services.GetRequiredService<IncomingShareUI>();
            incomingShareUI.ShareText(text);
        }).ConfigureAwait(false);
    }

    private async Task HandleFilesSend(string mimeType, ICollection<Uri> uris)
    {
        Log.LogInformation("About to send {Count} files of type '{MimeType}'", uris.Count, mimeType);
        await InvokeAsync(services => {
            var incomingShareUI = services.GetRequiredService<IncomingShareUI>();
            var fileInfos = uris
                .Select(c => {
                    var url = c.ToString()!;
                    if (AndroidContentDownloader.TryCreateAppHostRelativeUrl(url, out var relativeUrl))
                        return new IncomingShareFile(relativeUrl);
                    Log.LogWarning("Unsupported sent file url: '{Url}'", url);
                    return null;
                })
                .SkipNullItems()
                .ToArray();
            incomingShareUI.ShareFiles(fileInfos);
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
