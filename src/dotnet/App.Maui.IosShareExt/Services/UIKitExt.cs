using Intents;
using Microsoft.Maui.ApplicationModel;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class UIKitExt
{
    public static NSExtensionContext ExtensionContext => Platform.GetCurrentUIViewController()
        .Require()
        .ExtensionContext.Require();

    public static Task CloseApp(CancellationToken cancellationToken = default)
        => MainThread.InvokeOnMainThreadAsync(() => ExtensionContext.CompleteRequestAsync([])).WaitAsync(cancellationToken);

    public static void PlaySuccessHaptic()
        => MainThread.BeginInvokeOnMainThread(() => {
            var generator = new UINotificationFeedbackGenerator();
            generator.Prepare();
            generator.NotificationOccurred(UINotificationFeedbackType.Success);
        });

    public static Task OpenUrl(NSUrl url)
        => MainThread.InvokeOnMainThreadAsync(() => ExtensionContext.OpenUrlAsync(url));

    public static Task<ChatId?> GetSuggestedRecipient()
        => MainThread.InvokeOnMainThreadAsync(GetSuggestedRecipientUnsafe);

    private static ChatId? GetSuggestedRecipientUnsafe()
        => ExtensionContext.GetIntent() is INSendMessageIntent sendMessageIntent
            ? ChatId.ParseNullable(sendMessageIntent.ConversationIdentifier)
            : null;
}
