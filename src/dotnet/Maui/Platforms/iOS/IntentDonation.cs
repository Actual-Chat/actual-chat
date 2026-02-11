using ActualLab.Diagnostics;
using Intents;

namespace ActualChat.Maui;

public class IntentDonation(IServiceProvider services)
{
    private MomentClockSet Clocks => field ??= services.Clocks();
    private ILogger Log => field ??= services.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.ShareSuggestions);

    public async Task Donate(Chat.Chat chat, INImage? image, CancellationToken cancellationToken = default)
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
            nameComponents: null, // TODO: implement
            displayName: chat.Title,
            image: image,
            contactIdentifier: null,
            customIdentifier: chat.Id.Value);
    }

    private static INSendMessageIntent CreateSendMessageIntent(Chat.Chat chat, INImage? image)
    {
        var isPeer = chat.Kind is ChatKind.Peer;
        var speakableGroupName = !isPeer ? new INSpeakableString(chat.Title) : null;

        var intent = new INSendMessageIntent(
            recipients: isPeer ? [CreateRecipient(chat, image)] : [],
            outgoingMessageType: INOutgoingMessageType.Text,
            content: null,
            speakableGroupName: speakableGroupName,
            conversationIdentifier: chat.Id.Value,
            serviceName: "Voxt",
            sender: null,
            attachments: null);

        if (!isPeer && image != null)
            intent.SetImage(image, "speakableGroupName");

        return intent;
    }
}
