using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.Notifications;

/// <summary>
/// A notification's text before it's known who will read it. The author's own words are the same
/// string for everyone; anything this app words itself is composed in each recipient's language.
/// </summary>
public abstract record NotificationContent
{
    // Non-null only when the text can't vary by reader, which is what lets the fan-out skip
    // resolving a localizer for every recipient of a chat.
    public virtual string? SharedText => null;

    public abstract string Render(IStringLocalizer l);
}

/// <summary>
/// Text that belongs to its author rather than its reader - a message, or a sentence already
/// composed for the one recipient it's going to.
/// </summary>
public sealed record SharedNotificationContent(string Text) : NotificationContent
{
    public override string? SharedText => Text;
    public override string Render(IStringLocalizer l) => Text;
}

public sealed record ThreadCreatedNotificationContent(string ChatTitle) : NotificationContent
{
    public override string Render(IStringLocalizer l) => l.Notification_ThreadCreated_Format(ChatTitle);
}

public sealed record AttentionRequestedNotificationContent(string AuthorName) : NotificationContent
{
    public override string Render(IStringLocalizer l) => l.Notification_AttentionRequested_Format(AuthorName);
}

public sealed record VoiceChatStartedNotificationContent(IReadOnlyList<string> AuthorNames) : NotificationContent
{
    public override string Render(IStringLocalizer l)
        => NotificationHelper.GetVoiceChatStartedText(AuthorNames, l);
}

public sealed record IncomingCallNotificationContent(bool HasVideo) : NotificationContent
{
    public override string Render(IStringLocalizer l)
        => HasVideo ? l.Call_IncomingVideo : l.Call_Incoming;
}

/// <summary>
/// Stands in for an entry with no text of its own - see <see cref="EmptyEntryMarkupBuilder"/>.
/// </summary>
public sealed record EmptyEntryNotificationContent(
    ChatEntry Entry,
    MarkupConsumer Consumer,
    bool IsLiveLocation
) : NotificationContent
{
    public override string Render(IStringLocalizer l)
        => LocalizedEmptyEntryMarkupBuilder
            .Get(((IHasUILanguage)l).UILanguage)
            .Build(Entry, Consumer, IsLiveLocation)
            .ToReadableText(Consumer);
}
