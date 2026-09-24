using ActualChat.External;

namespace ActualChat.Mcp;

public static class McpModelExt
{
    public static McpIdRange<long> ToMcpModel(this Range<long> range)
        => range.IsEmptyOrNegative
            ? new McpIdRange<long>(range.Start, range.Start - 1)
            : new McpIdRange<long>(range.Start, range.End - 1);

    public static McpMessageStream ToMcpModel(this ChatEntryStream stream)
        => new(stream.Id.Value, stream.EntryId.LocalId, stream.Offset, stream.IsCompleted);

    public static McpVoiceStream ToMcpModel(this ChatVoiceStream stream)
        => new(stream.Id.Value, stream.EntryId?.LocalId, stream.TextOffset,
            stream.AudioBytes, stream.AudioDuration.TotalSeconds, stream.IsCompleted);

    public static Task<ExternalMessage> ToMcpModel(
        this ChatEntry entry,
        Dictionary<AuthorId, Author?> authorById,
        UrlMapper urlMapper,
        IMarkupParser markupParser,
        CancellationToken cancellationToken)
    {
        // A missing author (e.g. one who left) still needs an ExternalAuthor to fill in
        var author = authorById.GetValueOrDefault(entry.AuthorId)
            ?? new Author(entry.AuthorId) { Avatar = new Avatar(Symbol.Empty) };
        return entry.ToExternalMessage(
            author, includeText: true, urlMapper, AvatarUrl, markupParser, cancellationToken);

        Task<string?> AvatarUrl(Avatar avatar, CancellationToken _)
            => Task.FromResult(avatar.ToMcpPictureUrl(urlMapper));
    }

    public static McpMediaRef ToMcpMediaRef(this Media.Media media, Media.Media? thumbnail, UrlMapper urlMapper)
    {
        var contentType = media.ContentType;
        var url = urlMapper.ContentUrl(media.BlobId);
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        var previewUrl = isImage && urlMapper.HasImageProxy
            ? urlMapper.ImagePreviewUrl(url, Constants.Attachments.MaxResolution)
            : null;
        var thumbnailUrl = thumbnail is null ? null : urlMapper.ContentUrl(thumbnail.BlobId);
        return new McpMediaRef(
            media.Id.Value,
            GetKind(contentType),
            media.FileName,
            contentType,
            media.Length,
            media.Width,
            media.Height,
            url,
            previewUrl,
            thumbnailUrl);
    }

    public static McpMediaItem ToMcpModel(this VisualMediaItem item, UrlMapper urlMapper)
        => new(
            item.EntryId.LocalId,
            item.At.ToMcpMillis(),
            item.MediaId.Value,
            GetKind(item.ContentType),
            item.FileName,
            item.ContentType,
            item.Size,
            urlMapper.ContentUrl(item.BlobId),
            item.ThumbnailBlobId.IsNullOrEmpty() ? null : urlMapper.ContentUrl(item.ThumbnailBlobId));

    public static McpFileItem ToMcpModel(this FileItem item, UrlMapper urlMapper)
        => new(
            item.EntryId.LocalId,
            item.At.ToMcpMillis(),
            item.MediaId.Value,
            item.FileName,
            item.ContentType,
            item.Size,
            urlMapper.ContentUrl(item.BlobId));

    public static McpLinkItem ToMcpModel(this LinkItem item)
        => new(
            item.EntryId.LocalId,
            item.At.ToMcpMillis(),
            item.Url,
            item.LinkPreview?.Title.NullIfEmpty(),
            item.LinkPreview?.Description.NullIfEmpty());

    public static McpConversation ToMcpModel(this Conversation conversation)
        => new(
            conversation.Id.Value,
            conversation.Id.ChatId.Value,
            conversation.Id.StartEntryLid,
            conversation.EndEntryLid,
            conversation.StartsAt.ToMcpMillis(),
            conversation.EndsAt.ToMcpMillis(),
            conversation.Title,
            conversation.Description,
            conversation.Summary,
            conversation.MessageCount,
            conversation.AttachmentCount,
            conversation.AuthorIds.Select(a => a.Value).ToArray());

    public static McpChatDetails ToMcpDetails(this Chat.Chat chat, int memberCount, UrlMapper urlMapper)
        => new(
            chat.Id.Value,
            chat.Kind.ToString(),
            chat.Title,
            chat.Description,
            chat.IsPublic,
            chat.Id is PlaceChatId placeChatId ? placeChatId.PlaceId.Value : null,
            chat.Picture.ToMcpPictureUrl(urlMapper),
            memberCount,
            chat.Rules.Permissions.ToFlagNames());

    public static McpPlaceDetails ToMcpDetails(this Place place, int memberCount, UrlMapper urlMapper)
        => new(
            place.Id.Value,
            place.Title,
            place.Description,
            place.IsPublic,
            place.Picture.ToMcpPictureUrl(urlMapper),
            place.Background.ToMcpPictureUrl(urlMapper),
            memberCount,
            place.Rules.Permissions.ToFlagNames());

    public static async Task<McpMember[]> ToMcpMembers(
        this IEnumerable<AuthorId> authorIds,
        Func<AuthorId, Task<Author?>> getAuthor,
        Func<AuthorId, Task<Account?>> getAccount,
        IReadOnlySet<AuthorId> ownerIds)
    {
        var ids = authorIds.ToArray();
        var authors = await Task.WhenAll(ids.Select(getAuthor)).ConfigureAwait(false);
        var accounts = await Task.WhenAll(ids.Select(getAccount)).ConfigureAwait(false);
        var members = new List<McpMember>(ids.Length);
        for (var i = 0; i < ids.Length; i++) {
            if (authors[i] is not { HasLeft: false } author)
                continue;

            var isOwner = ownerIds.Contains(author.Id);
            members.Add(new McpMember(author.Id.Value, accounts[i]?.Id.Value, author.Avatar.Name, isOwner));
        }
        return members.ToArray();
    }

    public static McpInviteLink ToMcpModel(this Invite.Invite invite, UrlMapper urlMapper)
        => new(
            invite.Id.Value,
            urlMapper.ToAbsolute(Links.Invite(InviteLinkFormat.PrivateChat, invite.Id.Value)),
            invite.Remaining,
            invite.ExpiresOn.ToMcpMillis());

    public static long ToMcpMillis(this Moment moment)
        => (long)(moment - Moment.EpochStart).TotalMilliseconds;

    public static string[] ToFlagNames<TEnum>(this TEnum flags)
        where TEnum : struct, Enum
        => Enum.GetValues<TEnum>()
            .Where(f => Convert.ToInt64(f) != 0 && flags.HasFlag(f))
            .Select(f => f.ToString())
            .ToArray();

    public static McpAvatar ToMcpModel(this AvatarFull avatar, Symbol defaultAvatarId, UrlMapper urlMapper)
        => new(
            avatar.Id.Value,
            avatar.Name,
            avatar.Bio,
            avatar.ToMcpPictureUrl(urlMapper),
            avatar.Id == defaultAvatarId);

    public static string? ToMcpPictureUrl(this Avatar avatar, UrlMapper urlMapper)
        => avatar.Media is { } media
            ? urlMapper.ContentUrl(media.BlobId)
            : avatar.PictureUrl.NullIfEmpty();

    public static string? ToMcpPictureUrl(this Media.Media? media, UrlMapper urlMapper)
        => media is null ? null : urlMapper.ContentUrl(media.BlobId);

    private static string GetKind(string contentType)
        => contentType switch {
            _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
            _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "video",
            _ when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "audio",
            _ => "file",
        };
}
