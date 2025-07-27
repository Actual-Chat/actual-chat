using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using Android.OS;
using Java.Lang;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public static class IncomingShareHandler
{
    private static ILogger Log { get; } = StaticLog.For(typeof(IncomingShareHandler));

    public static void HandleIntent(Intent intent)
        => TryHandleSend(intent);

    // Private methods

    private static void TryHandleSend(Intent intent)
    {
        var action = intent.Action;
        if (!OrdinalEquals(action, Intent.ActionSend) &&
            !OrdinalEquals(action, Intent.ActionSendMultiple))
            return;

        Log.LogInformation("-> IncomingShare, send intent is detected. Action: {Action}", action);
        var mimeType = intent.Type ?? "";
        var hasExtraStream = intent.Extras?.ContainsKey(Intent.ExtraStream) ?? false;
        if (OrdinalEquals(action, Intent.ActionSend)) {
            if (hasExtraStream)
                HandleFilesSend(mimeType, GetStreams(intent, false));
            else if (OrdinalEquals(mimeType, System.Net.Mime.MediaTypeNames.Text.Plain))
                HandlePlainTextSend(intent.GetStringExtra(Intent.ExtraText));
            else
                Log.LogWarning("Unsupported send mime type: '{MimiType}'", mimeType);
        }
        else {
            if (hasExtraStream)
                HandleFilesSend(mimeType, GetStreams(intent, true));
            else
                Log.LogWarning("No extra streams for SendMultiple action. Mime type: '{MimiType}'", mimeType);
        }
    }

    private static void HandlePlainTextSend(string? text)
    {
        if (text.IsNullOrEmpty()) {
            Log.LogWarning("No text to send");
            return;
        }
        BeginInvokeOnMainThreadAsync(() => HandlePlainTextSendInternal(text));
    }

    private static void HandlePlainTextSendInternal(string text)
    {
        Log.LogInformation("About to send text: '{Text}'", text);
        _ = DispatchToBlazor(
                c => c.GetRequiredService<IncomingShareUI>().ShareText(text),
                "IncomingShareUI.ShareText(...)",
                true)
            .WithErrorLog(Log, "Failed send text")
            .SuppressExceptions();
    }

    private static void HandleFilesSend(string mimeType, IList? streams)
    {
        if (streams == null || streams.Count == 0) {
            Log.LogWarning("No file streams provided");
            return;
        }
        var uris = streams.OfType<Uri>().ToArray();
        if (uris.Length <= 0) {
            Log.LogWarning("No supported file streams provided. Type: {StreamType}",
                streams[0]?.GetType().FullName ?? "<null>");
            return;
        }
        BeginInvokeOnMainThreadAsync(() => HandleFilesSendInternal(mimeType, uris));
    }

    private static void HandleFilesSendInternal(string mimeType, Uri[] uris)
    {
        Log.LogInformation("About to send {Count} files of type '{MimeType}'", uris.Length, mimeType);
        _ = DispatchToBlazor(scopedServices => {
                    var downloader = scopedServices.GetRequiredService<AndroidContentDownloader>();
                    var fileInfos = downloader.ConvertToAttachFileInfos(uris);
                    var incomingShareUI = scopedServices.GetRequiredService<IncomingShareUI>();
                    incomingShareUI.ShareFiles(fileInfos);
                },
                "IncomingShareUI.ShareFiles(...)",
                true)
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

    // Ensures that the action will be queued for execution on the main thread.
    private static void BeginInvokeOnMainThreadAsync(Action action)
    {
        if (MainThread.IsMainThread) {
            var dispatcher = Dispatcher.GetForCurrentThread();
            if (dispatcher != null) {
                _ = dispatcher.DispatchAsync(action);
                return;
            }
        }
        action();
    }
}
