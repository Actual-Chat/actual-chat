namespace ActualChat.Mcp;

public static class McpModelExt
{
    public static McpIdRange<long> ToMcpModel(this Range<long> range)
        => range.IsEmptyOrNegative
            ? new McpIdRange<long>(range.Start, range.Start - 1)
            : new McpIdRange<long>(range.Start, range.End - 1);

    public static McpChatMessage ToMcpModel(
        this ChatEntry entry,
        Dictionary<AuthorId, Author?> authorById,
        UrlMapper urlMapper)
    {
        var authorName = authorById.GetValueOrDefault(entry.AuthorId)?.Avatar?.Name ?? "";
        var isStreaming = entry.IsContentStreaming;
        var text = isStreaming ? "" : entry.Content;
        var attachments = entry.Attachments.Select(a => a.ToMcpModel(urlMapper)).ToArray();
        return new McpChatMessage(
            entry.LocalId,
            entry.Version,
            (long)(entry.BeginsAt - Moment.EpochStart).TotalMilliseconds,
            entry.AuthorId.Value,
            authorName,
            entry.IsSystemEntry,
            isStreaming,
            entry.HasAudio,
            entry.IsRemoved,
            text,
            attachments);
    }

    public static McpAttachment ToMcpModel(this ChatEntryAttachment attachment, UrlMapper urlMapper)
    {
        var media = attachment.Media;
        var contentType = media.ContentType;
        var url = urlMapper.ContentUrl(media.BlobId);
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        var previewUrl = isImage && urlMapper.HasImageProxy
            ? urlMapper.ImagePreviewUrl(url, Constants.Attachments.MaxResolution)
            : null;
        var thumbnailUrl = attachment.ThumbnailMedia is { } thumbnail
            ? urlMapper.ContentUrl(thumbnail.BlobId)
            : null;
        return new McpAttachment(
            attachment.Id.Value,
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

    private static string GetKind(string contentType)
        => contentType switch {
            _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
            _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "video",
            _ when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "audio",
            _ => "file",
        };
}
