using ActualChat.Maui.Services;
using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using CoreSpotlight;
using Intents;

namespace ActualChat.Maui;

public class AppleIncomingShareSuggestions(IServiceProvider services) : IncomingShareSuggestions(services)
{
    private const string ViewChatActivityType = $"{MauiSettings.ReverseDomain}.viewChat";

    private NSUserActivity? _currentActivity;

    private MomentClockSet Clocks => field ??= Services.Clocks();
    private IconUI IconUI => field ??= Services.GetRequiredService<IconUI>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.ShareSuggestions);

    protected override async Task SuggestInternal(ContactId contactId, CancellationToken cancellationToken)
    {
        var contact = await Contacts.Get(Session, contactId, cancellationToken).Require().ConfigureAwait(false);
        using var image = await IconUI.GetIntentImage(contact, cancellationToken).ConfigureAwait(false);
        await DonateIntent(contact.Chat, image, cancellationToken).ConfigureAwait(false);
        DonateUserActivity(contact.Chat);
    }

    private async Task DonateIntent(Chat.Chat chat, INImage? image, CancellationToken cancellationToken = default)
    {
        try {
            var intent = CreateSendMessageIntent(chat, image);
            var now = Clocks.SystemClock.Now;
            await ChatIntents.Donate(intent, chat.Id, INInteractionDirection.Outgoing, now, cancellationToken)
                .ConfigureAwait(false);
            DebugLog?.LogInformation("Donated INSendMessageIntent for chat {ChatId}", chat.Id);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Failed to donate intent for chat {ChatId}", chat.Id);
        }
    }

    private static INSendMessageIntent CreateSendMessageIntent(Chat.Chat chat, INImage? image)
    {
        var isPeer = chat.Kind is ChatKind.Peer;
        var speakableGroupName = !isPeer ? new INSpeakableString(ChatIntents.FormatTitle(chat.Title)) : null;

        var intent = new INSendMessageIntent(
            recipients: isPeer ? [ChatIntents.NewPerson(chat, image)] : [],
            outgoingMessageType: INOutgoingMessageType.Text,
            content: null,
            speakableGroupName: speakableGroupName,
            conversationIdentifier: chat.Id.Value,
            // ReSharper disable once HeuristicUnreachableCode
            serviceName: MauiSettings.IsDevApp ? "Voxt (Dev)" : "Voxt",
            sender: null,
            attachments: null);

        if (!isPeer && image != null)
            intent.SetImage(image, "speakableGroupName");

        return intent;
    }

    private void DonateUserActivity(Chat.Chat chat)
    {
        try {
            _currentActivity?.ResignCurrent();

            var chatUrl = $"https://{MauiSettings.Host}/chat/{chat.Id}";
            var activity = new NSUserActivity(ViewChatActivityType) {
                Title = $"Chat with {ChatIntents.FormatTitle(chat.Title)}",
                EligibleForSearch = true,
                EligibleForPrediction = true,
                WebPageUrl = NSUrl.FromString(chatUrl),
                UserInfo = new NSDictionary("link", chatUrl),
                ContentAttributeSet = new CSSearchableItemAttributeSet {
                    DisplayName = ChatIntents.FormatTitle(chat.Title),
                },
            };
            activity.BecomeCurrent();
            _currentActivity = activity;

            DebugLog?.LogInformation("Donated NSUserActivity for chat {ChatId}", chat.Id);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to donate NSUserActivity for chat {ChatId}", chat.Id);
        }
    }
}
