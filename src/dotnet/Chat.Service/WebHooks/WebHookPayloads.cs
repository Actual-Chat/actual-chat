using ActualChat.External;
using ActualChat.WebHooks;
using ActualLab.Generators;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.Chat;

public sealed class WebHookPayloads(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private IMarkupParser MarkupParser => field ??= Services.GetRequiredService<IMarkupParser>();
    private UrlMapper UrlMapper => field ??= Services.GetRequiredService<UrlMapper>();
    private MomentClockSet Clocks => field ??= Services.Clocks();

    public async Task<string> Message(
        WebHook hook, WebHookEvents e, ChatEntry entry, ChatEntry? previous,
        AuthorFull author, CancellationToken cancellationToken)
    {
        var type = e.ToEventType();
        var eventKey = $"{entry.LocalId}:{entry.Version}";
        var chat = await ChatBlockFor(entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (e == WebHookEvents.MessageRemoved) {
            var removedAuthor = await author
                .ToExternalAuthor(UrlMapper, AvatarUrl, cancellationToken)
                .ConfigureAwait(false);
            var removedData = new { message = RemovedMessageBlock(entry, removedAuthor) };
            return Serialize(BuildEnvelope(hook, type, eventKey, chat, removedData));
        }

        var message = await entry
            .ToExternalMessage(author, hook.IncludeText, UrlMapper, AvatarUrl, MarkupParser, cancellationToken)
            .ConfigureAwait(false);
        var previousBlock = previous is null
            ? null
            : (object)new { version = previous.Version, text = hook.IncludeText ? previous.Content : null };
        return SerializeCapped(hook, type, eventKey, chat, message, m => new { message = m, previous = previousBlock });
    }

    public async Task<string> Reaction(
        WebHook hook, WebHookEvents e, Reaction reaction, ChatEntry entry,
        AuthorFull reactionAuthor, CancellationToken cancellationToken)
    {
        var type = e.ToEventType();
        var eventKey = reaction.Id.Value;
        var chat = await ChatBlockFor(entry.ChatId, cancellationToken).ConfigureAwait(false);
        var author = await reactionAuthor
            .ToExternalAuthor(UrlMapper, AvatarUrl, cancellationToken)
            .ConfigureAwait(false);
        var data = new { emoji = reaction.Emoji.Symbol, messageId = reaction.EntryId.LocalId, author };
        return Serialize(BuildEnvelope(hook, type, eventKey, chat, data));
    }

    public async Task<string> Member(
        WebHook hook, WebHookEvents e, AuthorFull author, CancellationToken cancellationToken)
    {
        var type = e.ToEventType();
        var eventKey = $"{author.Id.Value}:{author.Version}";
        var chat = type.StartsWith("place.")
            ? null
            : await ChatBlockFor(author.ChatId, cancellationToken).ConfigureAwait(false);
        var authorBlock = await author.ToExternalAuthor(UrlMapper, AvatarUrl, cancellationToken).ConfigureAwait(false);
        var data = new { author = authorBlock };
        return Serialize(BuildEnvelope(hook, type, eventKey, chat, data));
    }

    public async Task<string> ChatChanged(
        WebHook hook, WebHookEvents e, Chat chat, Chat? old,
        CancellationToken cancellationToken)
    {
        var type = e.ToEventType();
        var eventKey = chat.Version.ToString();
        var chatBlock = ChatBlock(chat);
        object data = type is "chat.created" or "chat.archived"
            ? new { chat = chatBlock }
            : await ChangedBlock(
                    chat.Title, chat.Description, chat.MediaId,
                    old?.Title, old?.Description, old?.MediaId, old is not null, cancellationToken)
                .ConfigureAwait(false);
        return Serialize(BuildEnvelope(hook, type, eventKey, chatBlock, data));
    }

    public async Task<string> PlaceChanged(
        WebHook hook, WebHookEvents e, Place place, Place? old,
        CancellationToken cancellationToken)
    {
        var type = e.ToEventType();
        var eventKey = place.Version.ToString();
        var data = await ChangedBlock(
                place.Title, place.Description, place.MediaId,
                old?.Title, old?.Description, old?.MediaId, old is not null, cancellationToken)
            .ConfigureAwait(false);
        return Serialize(BuildEnvelope(hook, type, eventKey, null, data));
    }

    public async Task<string> Notification(
        WebHook hook, Notification n, ChatEntry? entry, AuthorFull? author,
        CancellationToken cancellationToken)
    {
        var type = WebHookEvents.Notification.ToEventType();
        var eventKey = n.Id.Value;
        var chat = entry is null ? null : await ChatBlockFor(entry.ChatId, cancellationToken).ConfigureAwait(false);
        var message = entry is null || author is null
            ? null
            : await entry
                .ToExternalMessage(author, hook.IncludeText, UrlMapper, AvatarUrl, MarkupParser, cancellationToken)
                .ConfigureAwait(false);
        var kind = n.Kind.ToString().ToLowerInvariant();
        var text = hook.IncludeText ? n.Text : null;
        return SerializeCapped(
            hook, type, eventKey, chat, message, m => new { kind, title = n.Title, text, message = m });
    }

    public string Ping(WebHook hook, string sentBy)
    {
        var type = WebHookEvents.Ping.ToEventType();
        var eventKey = RandomStringGenerator.Default.Next();
        return Serialize(BuildEnvelope(hook, type, eventKey, null, new { sentBy }));
    }

    public static string DeliveryId(WebHookId hookId, string eventType, string eventKey)
        => $"{hookId.Value}:{eventType}:{eventKey}";

    // Private methods

    private string SerializeCapped(
        WebHook hook, string type, string eventKey, object? chat,
        ExternalMessage? message, Func<ExternalMessage?, object> buildData)
    {
        var json = Serialize(BuildEnvelope(hook, type, eventKey, chat, buildData(message)));
        if (json.Length <= Constants.WebHooks.MaxPayloadLength || message?.Text is not { Length: > 0 } text)
            return json;

        // Escaping (e.g. non-ASCII text) can inflate the JSON well past a plain char-count cut,
        // so keep halving the kept text until it fits or there's nothing left to cut.
        var cutLength = Math.Min(text.Length, Constants.WebHooks.MaxPayloadLength / 2);
        while (true) {
            var truncated = message with { Text = text[..cutLength], TextTruncated = true };
            json = Serialize(BuildEnvelope(hook, type, eventKey, chat, buildData(truncated)));
            if (json.Length <= Constants.WebHooks.MaxPayloadLength || cutLength == 0)
                return json;

            cutLength /= 2;
        }
    }

    private object BuildEnvelope(
        WebHook hook, string type, string eventKey, object? chat,
        object data)
        => new {
            id = DeliveryId(hook.Id, type, eventKey),
            type,
            timestamp = Clocks.SystemClock.Now.ToDateTime().ToString("O"),
            hook = new { id = hook.Id.Value, scope = hook.Scope.ToString().ToLowerInvariant(), scopeId = hook.ScopeId },
            chat,
            data,
        };

    private static string Serialize(object envelope)
        => JsonSerializer.Serialize(envelope, WebHookJson.Options);

    private async Task<object?> ChatBlockFor(ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        return chat is null ? null : ChatBlock(chat);
    }

    private object ChatBlock(Chat chat)
        => new {
            id = chat.Id.Value,
            title = chat.Title,
            kind = chat.Kind.ToString().ToLowerInvariant(),
            placeId = chat.Id is PlaceChatId placeChatId ? placeChatId.PlaceId.Value : null,
            url = UrlMapper.ToAbsolute(Links.Chat(chat.Id).Value),
        };

    private static object RemovedMessageBlock(ChatEntry entry, ExternalAuthor author)
        => new { id = entry.LocalId, author, isRemoved = true };

    private Task<string?> AvatarUrl(Avatar avatar, CancellationToken cancellationToken)
        => avatar.MediaId is { } mediaId
            ? MediaUrl(mediaId, cancellationToken)
            : Task.FromResult(avatar.PictureUrl.NullIfEmpty());

    private async Task<object> ChangedBlock(
        string title, string description, MediaId? mediaId,
        string? oldTitle, string? oldDescription, MediaId? oldMediaId, bool hasOld,
        CancellationToken cancellationToken)
    {
        // Chat.Picture / Place.Picture are populated for the UI only, so the backend diffs MediaId
        var pictureUrl = await MediaUrl(mediaId, cancellationToken).ConfigureAwait(false);
        var changed = new List<string>(3);
        if (!hasOld || title != oldTitle)
            changed.Add("title");
        if (!hasOld || description != oldDescription)
            changed.Add("description");
        if (!hasOld || mediaId != oldMediaId)
            changed.Add("pictureUrl");
        return new { changed = changed.ToArray(), title, description, pictureUrl };
    }

    private async Task<string?> MediaUrl(MediaId? mediaId, CancellationToken cancellationToken)
    {
        if (mediaId is null)
            return null;

        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        return media is null ? null : UrlMapper.ContentUrl(media.BlobId);
    }
}
