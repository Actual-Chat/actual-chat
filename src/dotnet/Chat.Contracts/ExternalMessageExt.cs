using ActualChat.External;

namespace ActualChat.Chat;

/// <summary>
/// Converts chat entries and authors to <see cref="ExternalMessage"/> — the shape shared by
/// MCP and outgoing web hooks.
/// </summary>
public static class ExternalMessageExt
{
    public static async Task<ExternalMessage> ToExternalMessage(
        this ChatEntry entry, Author author, bool includeText, UrlMapper urlMapper,
        Func<Avatar, CancellationToken, Task<string?>> avatarUrl, IMarkupParser markupParser,
        CancellationToken cancellationToken)
    {
        var authorBlock = await author.ToExternalAuthor(urlMapper, avatarUrl, cancellationToken).ConfigureAwait(false);
        var attachments = entry.Attachments.Select(a => a.ToExternalAttachment(urlMapper)).ToArray();
        var url = urlMapper.ToAbsolute(Links.Chat(entry.ChatId, entry.LocalId).Value);
        // In-progress transcription is never exposed
        var text = includeText && !entry.IsContentStreaming ? entry.Content : null;
        return new ExternalMessage(
            entry.LocalId,
            entry.Version,
            entry.BeginsAt.ToExternalMillis(),
            authorBlock,
            entry.IsSystemEntry,
            entry.IsContentStreaming,
            entry.HasAudio,
            entry.IsRemoved,
            text,
            null,
            attachments,
            entry.RepliedEntryLid,
            entry.Content.ToExternalMentions(markupParser),
            url,
            entry.ToExternalOrigin(author));
    }

    public static async Task<ExternalAuthor> ToExternalAuthor(
        this Author author, UrlMapper urlMapper, Func<Avatar, CancellationToken, Task<string?>> avatarUrl,
        CancellationToken cancellationToken)
    {
        var avatar = author.Avatar;
        var url = await avatarUrl(avatar, cancellationToken).ConfigureAwait(false);
        return new ExternalAuthor(author.Id.Value, avatar.Name, url);
    }

    // Private methods

    private static ExternalAttachment ToExternalAttachment(this ChatEntryAttachment attachment, UrlMapper urlMapper)
    {
        var media = attachment.Media;
        var contentType = media.ContentType;
        var url = urlMapper.ContentUrl(media.BlobId);
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        var previewUrl = isImage && urlMapper.HasImageProxy
            ? urlMapper.ImagePreviewUrl(url, Constants.Attachments.MaxResolution)
            : null;
        var thumbnailUrl = attachment.ThumbnailMedia is { } thumbnail ? urlMapper.ContentUrl(thumbnail.BlobId) : null;
        return new ExternalAttachment(
            media.Id.Value, GetMediaKind(contentType), media.FileName, contentType, media.Length,
            media.Width, media.Height, url, previewUrl, thumbnailUrl);
    }

    private static string[] ToExternalMentions(this string content, IMarkupParser markupParser)
    {
        var markup = markupParser.Parse(content);
        return MentionExtractor.Instance.GetMentionIds(markup)
            .Where(m => m.Kind == MentionKind.Author)
            .Select(m => ((AuthorId)m.Target).Value)
            .ToArray();
    }

    private static ExternalOrigin ToExternalOrigin(this ChatEntry entry, Author author)
    {
        // Below Sherlock's id: a hook bot. The id alone decides, because a regular user id may
        // start with the bot prefix by chance
        if (entry.AuthorId.LocalId < Constants.User.Sherlock.AuthorLocalId) {
            if (author is AuthorFull { UserId: var userId } && WebHookId.TryParseBotUserId(userId, out var hookId))
                return new ExternalOrigin("webhook", hookId.Value);

            // The base Author carries no UserId, so the hook id is unavailable here
            return new ExternalOrigin("webhook");
        }

        // Mirrors Bots.IsBot (Chat.Service, unreachable from Chat.Contracts): a negative local id is a bot's
        return entry.AuthorId.LocalId < 0 ? new ExternalOrigin("bot")
            : entry.IsViaApi ? new ExternalOrigin("api")
            : new ExternalOrigin("user");
    }

    private static long ToExternalMillis(this Moment moment)
        => (long)(moment - Moment.EpochStart).TotalMilliseconds;

    private static string GetMediaKind(string contentType)
        => contentType switch {
            _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
            _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "video",
            _ when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "audio",
            _ => "file",
        };
}
