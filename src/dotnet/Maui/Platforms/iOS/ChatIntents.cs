using Intents;

namespace ActualChat.Maui;

/// <summary>
/// What every chat-related <see cref="INIntent"/> donation shares: the <see cref="INPerson"/>
/// standing for the chat and the donation itself.
/// </summary>
public static class ChatIntents
{
    public static INPerson NewPerson(Chat.Chat chat, INImage? image)
        => new(
            personHandle: new INPersonHandle(chat.Id.Value, INPersonHandleType.Unknown),
            nameComponents: null,
            displayName: FormatTitle(chat.Title),
            image: image,
            contactIdentifier: null,
            customIdentifier: chat.Id.Value);

    public static Task Donate(
        INIntent intent, ChatId chatId, INInteractionDirection direction, Moment now,
        CancellationToken cancellationToken)
    {
        var interaction = new INInteraction(intent, null) {
            Direction = direction,
            Identifier = $"{chatId}-{now.EpochOffsetTicks}",
            GroupIdentifier = chatId.Value,
        };
        return interaction.DonateInteractionAsync().WaitAsync(cancellationToken);
    }

    public static string FormatTitle(string title)
        // ReSharper disable once HeuristicUnreachableCode
 #pragma warning disable CS0162 // Unreachable code detected
        => MauiSettings.IsDevApp ? $"🛠{title}️" : title;
 #pragma warning restore CS0162 // Unreachable code detected
}
