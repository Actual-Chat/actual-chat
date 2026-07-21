using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial record ChatNews(
    [property: DataMember, Key(0)] Range<long> TextEntryLidRange,
    [property: DataMember, Key(1)] ChatEntry? LastTextEntry = null)
{
    // Returns a copy with LastTextEntry trimmed down to what chat list items actually
    // render: scalars and flags stay; link previews, forward info, the audio time map,
    // and media payloads (except the content type and file name, which drive the
    // attachment icons and placeholder texts) are stripped.
    public ChatNews ToSlim()
        => LastTextEntry is { } entry ? this with { LastTextEntry = ToSlim(entry) } : this;

    // Private methods

    private static ChatEntry ToSlim(ChatEntry entry)
        => entry with {
            ClientId = "",
            Forwarded = null,
            LinkPreviewIds = [],
            LinkPreviews = [],
            Audio = entry.Audio is { } audio ? audio with { TimeMap = default } : null,
            Attachments = entry.Attachments.Length == 0
                ? entry.Attachments
                : entry.Attachments.Select(ToSlim).ToArray(),
        };

    private static ChatEntryAttachment ToSlim(ChatEntryAttachment attachment)
        => attachment with {
            Media = attachment.Media is { } media
                ? new Media.Media(media.Id) {
                    ContentType = media.ContentType,
                    FileName = media.FileName,
                }
                : attachment.Media,
            ThumbnailMedia = null,
        };
}
