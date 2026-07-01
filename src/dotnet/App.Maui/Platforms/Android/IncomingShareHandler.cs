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
        if (action != Intent.ActionSend &&
            action != Intent.ActionSendMultiple)
            return;

        Log.LogInformation("-> IncomingShare, send intent is detected. Action: {Action}", action);

        var shortcutChatIdString = intent.GetStringExtra(Intent.ExtraShortcutId);
        ChatId? targetChatId = null;
        if (!shortcutChatIdString.IsNullOrEmpty()) {
            targetChatId = ChatId.TryParse(shortcutChatIdString);
            if (targetChatId is null)
                Log.LogWarning("Share via shortcut: failed to parse shortcut id '{ShortcutId}' as ChatId", shortcutChatIdString);
            else
                Log.LogInformation("Share triggered via shortcut for chat: {ChatId}", targetChatId);
        }

        var mimeType = intent.Type ?? "";
        var canPersistGrant = intent.Flags.HasFlag(ActivityFlags.GrantPersistableUriPermission);
        var hasExtraStream = intent.Extras?.ContainsKey(Intent.ExtraStream) ?? false;
        if (action == Intent.ActionSend) {
            if (hasExtraStream)
                HandleFilesSend(mimeType, GetStreams(intent, false), targetChatId, canPersistGrant);
            else if (mimeType == System.Net.Mime.MediaTypeNames.Text.Plain)
                HandlePlainTextSend(intent.GetStringExtra(Intent.ExtraText), targetChatId);
            else
                Log.LogWarning("Unsupported send mime type: '{MimiType}'", mimeType);
        }
        else {
            if (hasExtraStream)
                HandleFilesSend(mimeType, GetStreams(intent, true), targetChatId, canPersistGrant);
            else
                Log.LogWarning("No extra streams for SendMultiple action. Mime type: '{MimiType}'", mimeType);
        }
    }

    private static void HandlePlainTextSend(string? text, ChatId? targetChatId)
    {
        if (text.IsNullOrEmpty()) {
            Log.LogWarning("No text to send");
            return;
        }
        BeginInvokeOnMainThreadAsync(() => HandlePlainTextSendInternal(text, targetChatId));
    }

    private static void HandlePlainTextSendInternal(string text, ChatId? targetChatId)
    {
        Log.LogInformation("About to send text: '{Text}'", text.ToPrivate());
        _ = DispatchToBlazor(
                c => c.GetRequiredService<IncomingShareUI>().ShareText(text, targetChatId),
                "IncomingShareUI.ShareText(...)",
                true)
            .WithErrorLog(Log, "Failed send text")
            .SuppressExceptions();
    }

    private static void HandleFilesSend(string mimeType, IList? streams, ChatId? targetChatId, bool canPersistGrant)
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
        BeginInvokeOnMainThreadAsync(() => HandleFilesSendInternal(mimeType, uris, targetChatId, canPersistGrant));
    }

    private static void HandleFilesSendInternal(string mimeType, Uri[] uris, ChatId? targetChatId, bool canPersistGrant)
    {
        Log.LogInformation("About to send {Count} files of type '{MimeType}'", uris.Length, mimeType);
        _ = DispatchToBlazor(scopedServices => {
                    var downloader = scopedServices.GetRequiredService<AndroidContentDownloader>();
                    var fileInfos = downloader.ConvertToAttachFileInfos(uris, canPersistGrant);
                    var incomingShareUI = scopedServices.GetRequiredService<IncomingShareUI>();
                    incomingShareUI.ShareFiles(fileInfos, targetChatId);
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
