namespace ActualChat.Notifications;

/// <summary>
/// Reads a chat entry as the text a notification will carry, along with the mentions in it and
/// whether the markup layer stood text in for an entry that had none of its own.
/// </summary>
public sealed class NotificationTextComposer(IServiceProvider services)
{
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();

    public async ValueTask<(string Content, HashSet<MentionRef> MentionIds, bool IsSubstituted)> GetText(
        ChatEntry entry,
        MarkupConsumer consumer,
        CancellationToken cancellationToken)
    {
        var chatMarkupHub = ChatMarkupHubFactory[entry.ChatId];
        var (markup, isSubstituted) = await chatMarkupHub
            .GetMarkupWithSubstitution(entry, null, consumer, cancellationToken)
            .ConfigureAwait(false);
        var mentionIds = MentionExtractor.Instance.GetMentionIds(markup);
        return (markup.ToReadableText(consumer), mentionIds, isSubstituted);
    }
}
