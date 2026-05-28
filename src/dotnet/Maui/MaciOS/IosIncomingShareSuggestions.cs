using ActualChat.Maui.Services;
using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using CoreSpotlight;
using Intents;

namespace ActualChat.Maui;

public class IosIncomingShareSuggestions(IServiceProvider services) : IncomingShareSuggestions(services)
{
    private const string ViewChatActivityType = $"{MauiSettings.ReverseDomain}.viewChat";

    private NSUserActivity? _currentActivity;

    private MomentClockSet Clocks => field ??= Services.Clocks();
    private IconUI IconUI => field ??= Services.GetRequiredService<IconUI>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.ShareSuggestions);

    protected override async Task SuggestInternal(ContactId contactId, CancellationToken cancellationToken)
    {
        var contact = await Contacts.Get(Session, contactId, cancellationToken).Require().ConfigureAwait(false);
        var loadedImage = await IconUI.Get(contact.GetIconQuery(avatarSize: 160, renderAvatarTitle: true), cancellationToken).ConfigureAwait(false);
        // NOTE: Embed image data into the intent rather than referencing a file URL,
        // because iOS may purge CacheDirectory at any time, breaking file references.
        using var inImage = loadedImage is not null
            ? INImage.FromData(NSData.FromFile(loadedImage.FilePath))
            : null;
        await DonateIntent(contact.Chat, inImage, cancellationToken).ConfigureAwait(false);
        DonateUserActivity(contact.Chat);
    }

    private async Task DonateIntent(Chat.Chat chat, INImage? image, CancellationToken cancellationToken = default)
    {
        try {
            var intent = CreateSendMessageIntent(chat, image);

            var interaction = new INInteraction(intent, null) {
                Direction = INInteractionDirection.Outgoing,
                Identifier = $"{chat.Id}-{Clocks.SystemClock.UtcNow.Ticks}",
                GroupIdentifier = chat.Id.Value,
            };

            await interaction.DonateInteractionAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            DebugLog?.LogInformation("Donated INSendMessageIntent for chat {ChatId}", chat.Id);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Failed to donate intent for chat {ChatId}", chat.Id);
        }
    }

    private static INPerson CreateRecipient(Chat.Chat chat, INImage? image)
    {
        var handle = new INPersonHandle(chat.Id.Value, INPersonHandleType.Unknown);
        return new INPerson(
            personHandle: handle,
            nameComponents: null,
            displayName: FormatTitle(chat.Title),
            image: image,
            contactIdentifier: null,
            customIdentifier: chat.Id.Value);
    }

    private static INSendMessageIntent CreateSendMessageIntent(Chat.Chat chat, INImage? image)
    {
        var isPeer = chat.Kind is ChatKind.Peer;
        var speakableGroupName = !isPeer ? new INSpeakableString(FormatTitle(chat.Title)) : null;

        var intent = new INSendMessageIntent(
            recipients: isPeer ? [CreateRecipient(chat, image)] : [],
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
                Title = $"Chat with {FormatTitle(chat.Title)}",
                EligibleForSearch = true,
                EligibleForPrediction = true,
                WebPageUrl = NSUrl.FromString(chatUrl),
                UserInfo = new NSDictionary("link", chatUrl),
                ContentAttributeSet = new CSSearchableItemAttributeSet {
                    DisplayName = FormatTitle(chat.Title),
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

    private static string FormatTitle(string title)
        // ReSharper disable once HeuristicUnreachableCode
 #pragma warning disable CS0162 // Unreachable code detected
        => MauiSettings.IsDevApp ? $"🛠{title}️" : title;
 #pragma warning restore CS0162 // Unreachable code detected
}
