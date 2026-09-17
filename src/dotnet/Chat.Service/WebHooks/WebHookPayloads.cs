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
            var removedAuthor = await ToExternalAuthor(author, cancellationToken).ConfigureAwait(false);
            var removedData = new { message = RemovedMessageBlock(entry, removedAuthor) };
            return Serialize(BuildEnvelope(hook, type, eventKey, chat, removedData));
        }

        var message = await ToExternalMessage(entry, author, hook.IncludeText, cancellationToken).ConfigureAwait(false);
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
        var author = await ToExternalAuthor(reactionAuthor, cancellationToken).ConfigureAwait(false);
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
        var data = new { author = await ToExternalAuthor(author, cancellationToken).ConfigureAwait(false) };
        return Serialize(BuildEnvelope(hook, type, eventKey, chat, data));
    }

    public Task<string> ChatChanged(
        WebHook hook, WebHookEvents e, Chat chat, Chat? old,
        CancellationToken cancellationToken)
    {
        var type = e.ToEventType();
        var eventKey = chat.Version.ToString();
        var chatBlock = ChatBlock(chat);
        object data = type is "chat.created" or "chat.archived"
            ? new { chat = chatBlock }
            : ChangedBlock(
                chat.Title, chat.Description, chat.Picture,
                old?.Title, old?.Description, old?.Picture, old is not null);
        return Task.FromResult(Serialize(BuildEnvelope(hook, type, eventKey, chatBlock, data)));
    }

    public Task<string> PlaceChanged(
        WebHook hook, WebHookEvents e, Place place, Place? old,
        CancellationToken cancellationToken)
    {
        var type = e.ToEventType();
        var eventKey = place.Version.ToString();
        var data = ChangedBlock(
            place.Title, place.Description, place.Picture,
            old?.Title, old?.Description, old?.Picture, old is not null);
        return Task.FromResult(Serialize(BuildEnvelope(hook, type, eventKey, null, data)));
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
            : await ToExternalMessage(entry, author, hook.IncludeText, cancellationToken).ConfigureAwait(false);
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

        var truncated = message with {
            Text = text[..Math.Min(text.Length, Constants.WebHooks.MaxPayloadLength / 2)],
            TextTruncated = true,
        };
        return Serialize(BuildEnvelope(hook, type, eventKey, chat, buildData(truncated)));
    }

    private object BuildEnvelope(
        WebHook hook, string type, string eventKey, object? chat, object data)
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

    private async Task<ExternalMessage> ToExternalMessage(
        ChatEntry entry, AuthorFull author, bool includeText, CancellationToken cancellationToken)
    {
        var authorBlock = await ToExternalAuthor(author, cancellationToken).ConfigureAwait(false);
        var attachments = entry.Attachments.Select(ToExternalAttachment).ToArray();
        var url = UrlMapper.ToAbsolute(Links.Chat(entry.ChatId, entry.LocalId).Value);
        return new ExternalMessage(
            entry.LocalId,
            entry.Version,
            ToUnixMillis(entry.BeginsAt),
            authorBlock,
            entry.IsSystemEntry,
            entry.IsContentStreaming,
            entry.HasAudio,
            entry.IsRemoved,
            includeText ? entry.Content : null,
            null,
            attachments,
            entry.RepliedEntryLid,
            GetMentions(entry.Content),
            url,
            GetOrigin(entry));
    }

    private static object RemovedMessageBlock(ChatEntry entry, ExternalAuthor author)
        => new { id = entry.LocalId, author, isRemoved = true };

    private async Task<ExternalAuthor> ToExternalAuthor(AuthorFull author, CancellationToken cancellationToken)
    {
        var avatar = author.Avatar;
        string? avatarUrl;
        if (avatar.MediaId is { } mediaId) {
            var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
            avatarUrl = media is null ? null : UrlMapper.ContentUrl(media.BlobId);
        }
        else
            avatarUrl = avatar.PictureUrl.NullIfEmpty();
        return new ExternalAuthor(author.Id.Value, avatar.Name, avatarUrl);
    }

    private ExternalAttachment ToExternalAttachment(ChatEntryAttachment attachment)
    {
        var media = attachment.Media;
        var contentType = media.ContentType;
        var url = UrlMapper.ContentUrl(media.BlobId);
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        var previewUrl = isImage && UrlMapper.HasImageProxy
            ? UrlMapper.ImagePreviewUrl(url, Constants.Attachments.MaxResolution)
            : null;
        var thumbnailUrl = attachment.ThumbnailMedia is { } thumbnail ? UrlMapper.ContentUrl(thumbnail.BlobId) : null;
        return new ExternalAttachment(
            media.Id.Value, GetMediaKind(contentType), media.FileName, contentType, media.Length,
            media.Width, media.Height, url, previewUrl, thumbnailUrl);
    }

    private string[] GetMentions(string content)
    {
        var markup = MarkupParser.Parse(content);
        return MentionExtractor.Instance.GetMentionIds(markup)
            .Where(m => m.Kind == MentionKind.Author)
            .Select(m => ((AuthorId)m.Target).Value)
            .ToArray();
    }

    private object ChangedBlock(
        string title, string description, Media.Media? picture,
        string? oldTitle, string? oldDescription, Media.Media? oldPicture, bool hasOld)
    {
        var pictureUrl = PictureUrl(picture);
        var changed = new List<string>(3);
        if (!hasOld || title != oldTitle)
            changed.Add("title");
        if (!hasOld || description != oldDescription)
            changed.Add("description");
        if (!hasOld || pictureUrl != PictureUrl(oldPicture))
            changed.Add("pictureUrl");
        return new { changed = changed.ToArray(), title, description, pictureUrl };
    }

    private string? PictureUrl(Media.Media? media)
        => media is null ? null : UrlMapper.ContentUrl(media.BlobId);

    private static long ToUnixMillis(Moment moment)
        => (long)(moment - Moment.EpochStart).TotalMilliseconds;

    private static ExternalOrigin GetOrigin(ChatEntry entry)
        => Bots.IsBot(entry.AuthorId) ? new ExternalOrigin("bot")
            : entry.IsViaApi ? new ExternalOrigin("api")
            : new ExternalOrigin("user");

    private static string GetMediaKind(string contentType)
        => contentType switch {
            _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
            _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "video",
            _ when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "audio",
            _ => "file",
        };
}
