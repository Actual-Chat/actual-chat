namespace ActualChat.Notifications;

/// <summary>
/// Reads a chat entry as the content a notification will carry, along with the mentions in it.
/// The author's own words are shared by every reader; an entry with none is worded per reader.
/// </summary>
public sealed class NotificationTextComposer(IServiceProvider services)
{
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromSeconds(15);

    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private ISharedLocationsBackend SharedLocationsBackend { get; }
        = services.GetRequiredService<ISharedLocationsBackend>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log { get; } = services.LogFor<NotificationTextComposer>();

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

    public async ValueTask<IReadOnlyDictionary<Language, string>> ComposeTextByLanguage(
        ChatEntry entry,
        MarkupConsumer consumer,
        IReadOnlyCollection<Language> languages,
        CancellationToken cancellationToken)
    {
        // Only the author's own words: anything else is already worded in the reader's language
        if (languages.Count == 0 || IsTextless(entry) || !entry.SupportsTranslation(false))
            return ImmutableDictionary<Language, string>.Empty;

        var chatMarkupHub = ChatMarkupHubFactory[entry.ChatId];
        var texts = await languages
            .Select(async language => {
                var translation = await GetTranslation(entry, language, cancellationToken).ConfigureAwait(false);
                if (translation is null || translation.MatchesOriginal(entry.Content))
                    return (Language: language, Text: (string?)null);

                var markup = await chatMarkupHub
                    .GetMarkup(entry, translation, consumer, cancellationToken)
                    .ConfigureAwait(false);
                return (Language: language, Text: markup.ToReadableText(consumer));
            })
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return texts
            .Where(x => x.Text is not null)
            .ToDictionary(x => x.Language, x => x.Text!);
    }

    // Private methods

    private static bool IsTextless(ChatEntry entry)
        // TODO: 2026-07, drop the HasLocation term when all clients support location entries:
        // until then a location entry's Content is a maps-link fallback for old clients.
        => entry is { IsSystemEntry: false, HasAudio: false }
            && (entry.HasLocation || entry.Content.IsNullOrEmpty());

    // Not IsLive(now): Duration is immutable, so this says "was shared live" and the wording
    // can't change under a reader when the share later expires.
    private async ValueTask<bool> IsLiveLocation(ChatEntry entry, CancellationToken cancellationToken)
    {
        if (entry.LocationId is not { } locationId)
            return false;

        var location = await SharedLocationsBackend.Get(locationId, cancellationToken).ConfigureAwait(false);
        return location is { Duration.Ticks: > 0 };
    }

    private async Task<Translation?> GetTranslation(
        ChatEntry entry, Language language, CancellationToken cancellationToken)
    {
        // Stored under the id the client reads the entry's translation by, so the chat opens on it.
        // A push the translator holds back or fails is still owed: it goes out as written.
        var command = new TranslationsBackend_Translate(TranslationSourceId.New(entry.Id), language, false, true);
        try {
            // Outermost: nested, the languages translated in parallel would share the DB connection
            // of the command this runs in
            return await Commander.Call(command, true, cancellationToken)
                .WaitAsync(TranslationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to translate entry #{EntryId} to '{Language}'", entry.Id, language.Value);
            return null;
        }
    }
}
