using ActualChat.Maui.Services;
using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using Intents;

namespace ActualChat.Maui;

public class IosIncomingShareSuggestions(IServiceProvider services) : IncomingShareSuggestions(services)
{
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private IconUI IconUI => field ??= Services.GetRequiredService<IconUI>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.ShareSuggestions);

    protected override async Task SuggestInternal(ContactId contactId, CancellationToken cancellationToken)
    {
        var contact = await Contacts.Get(Session, contactId, cancellationToken).Require().ConfigureAwait(false);
        var loadedImage = await IconUI.Get(contact.GetIconQuery(), cancellationToken).ConfigureAwait(false);
        // NOTE: Embed image data into the intent rather than referencing a file URL,
        // because iOS may purge CacheDirectory at any time, breaking file references.
        using var inImage = loadedImage is not null
            ? INImage.FromData(NSData.FromFile(loadedImage.FilePath))
            : null;
        await DonateIntent(contact.Chat, inImage, cancellationToken).ConfigureAwait(false);
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

    private static string FormatTitle(string title)
        // ReSharper disable once HeuristicUnreachableCode
        => MauiSettings.IsDevApp ? $"🛠{title}️" : title;
}
