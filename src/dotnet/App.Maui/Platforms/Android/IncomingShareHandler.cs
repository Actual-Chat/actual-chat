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

        Log.LogInformation("-> IncomingShare, send intent is detected. Action: {Action}", action);
        var mimeType = intent.Type ?? "";
        var hasExtraStream = intent.Extras?.ContainsKey(Intent.ExtraStream) ?? false;
        if (OrdinalEquals(action, Intent.ActionSend)) {
            if (hasExtraStream)
                _ = HandleFilesSend(mimeType, GetStreams(intent, false));
            else if (OrdinalEquals(mimeType, System.Net.Mime.MediaTypeNames.Text.Plain))
                _ = HandlePlainTextSend(intent.GetStringExtra(Intent.ExtraText));
            else
                Log.LogWarning("Unsupported send mime type: '{MimiType}'", mimeType);
        }
        else {
            if (hasExtraStream)
                _ = HandleFilesSend(mimeType, GetStreams(intent, true));
            else
                Log.LogWarning("No extra streams for SendMultiple action. Mime type: '{MimiType}'", mimeType);
        }
    }

    private Task HandlePlainTextSend(string? text)
    {
        if (text.IsNullOrEmpty()) {
            Log.LogWarning("No text to send");
            return Task.CompletedTask;
        }
        Log.LogInformation("About to send text: '{Text}'", text);
        return DispatchToBlazor(
            c => c.GetRequiredService<IncomingShareUI>().ShareText(text),
            "IncomingShareUI.ShareText(...)", true)
            .WithErrorLog(Log, "Failed send text")
            .SuppressExceptions();
    }

    private Task HandleFilesSend(string mimeType, IList? streams)
    {
        if (streams == null || streams.Count == 0) {
            Log.LogWarning("No file streams provided");
            return Task.CompletedTask;
        }
        var uris = streams.OfType<Uri>().ToArray();
        if (uris.Length <= 0) {
            Log.LogWarning("No supported file streams provided. Type: {StreamType}",
                streams[0]?.GetType().FullName ?? "<null>");
            return Task.CompletedTask;
        }
        return HandleFilesSend(mimeType, (ICollection<Uri>)uris);
    }

    private Task HandleFilesSend(string mimeType, ICollection<Uri> uris)
    {
        Log.LogInformation("About to send {Count} files of type '{MimeType}'", uris.Count, mimeType);
        return DispatchToBlazor(scopedServices => {
            var incomingShareUI = scopedServices.GetRequiredService<IncomingShareUI>();
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
        }, "IncomingShareUI.ShareFiles(...)", true)
        .WithErrorLog(Log, "Failed send files")
        .SuppressExceptions();
    }

    private static IList? GetStreams(Intent intent, bool multipleStreams)
    {
        if (!multipleStreams) {
            var stream = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
                ? intent.GetParcelableExtra(Intent.ExtraStream, Class.FromType(typeof(Uri)))
#pragma warning disable CA1422
                : intent.GetParcelableExtra(Intent.ExtraStream);
#pragma warning restore CA1422
            var streams = new object?[] { stream };
            return streams;
        }
        else {
            var streams = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
                ? intent.GetParcelableArrayListExtra(Intent.ExtraStream, Class.FromType(typeof(Uri)))
#pragma warning disable CA1422
                : intent.GetParcelableArrayListExtra(Intent.ExtraStream);
#pragma warning restore CA1422
            return streams;
        }
    }
}
