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

    public static ChatId? GetSuggestedRecipient()
        => ExtensionContext.GetIntent() is INSendMessageIntent sendMessageIntent
            ? ChatId.ParseNullable(sendMessageIntent.ConversationIdentifier)
            : null;
}
