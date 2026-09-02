namespace ActualChat.Notifications;

/// <summary>
/// Reads a chat entry as the content a notification will carry, along with the mentions in it.
/// The author's own words are shared by every reader; an entry with none is worded per reader.
/// </summary>
public sealed class NotificationTextComposer(IServiceProvider services)
{
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private ISharedLocationsBackend SharedLocationsBackend { get; }
        = services.GetRequiredService<ISharedLocationsBackend>();

    public async ValueTask<(NotificationContent Content, HashSet<MentionRef> MentionIds)> Compose(
        ChatEntry entry,
        MarkupConsumer consumer,
        CancellationToken cancellationToken)
    {
        if (IsTextless(entry)) {
            var isLiveLocation = await IsLiveLocation(entry, cancellationToken).ConfigureAwait(false);
            return (new EmptyEntryNotificationContent(entry, consumer, isLiveLocation), []);
        }

        var chatMarkupHub = ChatMarkupHubFactory[entry.ChatId];
        var markup = await chatMarkupHub.GetMarkup(entry, consumer, cancellationToken).ConfigureAwait(false);
        var mentionIds = MentionExtractor.Instance.GetMentionIds(markup);
        return (new SharedNotificationContent(markup.ToReadableText(consumer)), mentionIds);
    }

    // Private methods

    private static bool IsTextless(ChatEntry entry)
        // TODO: 2026-07, drop the HasLocation term when all clients support location entries:
        // until then a location entry's Content is a maps-link fallback for old clients.
        => entry is { IsSystemEntry: false, HasAudio: false }
            && (entry.HasLocation || entry.Content.IsNullOrEmpty());

    private async ValueTask<bool> IsLiveLocation(ChatEntry entry, CancellationToken cancellationToken)
    {
        if (entry.LocationId is not { } locationId)
            return false;

        var location = await SharedLocationsBackend.Get(locationId, cancellationToken).ConfigureAwait(false);
        return location is { Duration.Ticks: > 0 };
    }
}
