using ActualChat.Maui;
using Intents;
using Microsoft.Maui.ApplicationModel;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class UIKitExt
{
    public static NSExtensionContext ExtensionContext => Platform.GetCurrentUIViewController()
        .Require()
        .ExtensionContext.Require();

    public static Task CloseApp(CancellationToken cancellationToken = default)
        => MauiMainThread.DispatchToMainThread(() => ExtensionContext.CompleteRequestAsync([]))
            .WaitAsync(cancellationToken);

    public static void PlaySuccessHaptic()
        => MauiMainThread.BeginDispatchToMainThread(() => {
            var generator = new UINotificationFeedbackGenerator();
            generator.Prepare();
            generator.NotificationOccurred(UINotificationFeedbackType.Success);
        });

    public static Task OpenUrl(NSUrl url, CancellationToken cancellationToken = default)
        => MauiMainThread.DispatchToMainThread(
                () => UIApplication.SharedApplication.OpenUrlAsync(url, new UIApplicationOpenUrlOptions()))
            .WaitAsync(cancellationToken);

    public static Task<ChatId?> GetSuggestedRecipient()
        => MauiMainThread.DispatchToMainThread(GetSuggestedRecipientUnsafe);

    private static ChatId? GetSuggestedRecipientUnsafe()
        => ExtensionContext.GetIntent() is INSendMessageIntent sendMessageIntent
            ? ChatId.ParseNullable(sendMessageIntent.ConversationIdentifier)
            : null;

}
