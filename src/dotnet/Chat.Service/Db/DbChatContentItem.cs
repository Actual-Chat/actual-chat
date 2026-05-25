using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("ChatContentItems")]
[Index(nameof(ChatId), nameof(Kind), nameof(At), nameof(EntryLocalId), nameof(LocalIndex))]
[Index(nameof(EntryId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatContentItem : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const char IdSeparator = ':';

    public DbChatContentItem() { }
    public DbChatContentItem(ChatContentItem model) => UpdateFrom(model);

    // (EntryId, Kind, LocalIndex)
    [DbKey] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public ChatContentKind Kind { get; set; }
    public string EntryId { get; set; } = "";
    public long EntryLocalId { get; set; }
    public int LocalIndex { get; set; }

    public DateTime At {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public string? MediaId { get; set; }
    public string BlobId { get; set; } = "";
    public string? ThumbnailMediaId { get; set; }
    public string ThumbnailBlobId { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string LinkPreviewId { get; set; } = "";

    public static string ComposeId(ChatEntryId entryId, ChatContentKind kind, int localIndex)
        => $"{entryId}{IdSeparator}{(int)kind}{IdSeparator}{localIndex}";

    public ChatContentItem ToModel()
        => new() {
            Id = Id,
            Version = Version,
            Kind = Kind,
            EntryId = ChatEntryId.Parse(EntryId),
            LocalIndex = LocalIndex,
            At = new Moment(At),
            MediaId = ActualChat.MediaId.ParseNullable(MediaId),
            BlobId = BlobId,
            ThumbnailMediaId = ActualChat.MediaId.ParseNullable(ThumbnailMediaId),
            ThumbnailBlobId = ThumbnailBlobId,
            ContentType = ContentType,
            FileName = FileName,
            Size = Size,
            LinkPreviewId = LinkPreviewId,
        };

    public void UpdateFrom(ChatContentItem model)
    {
        var id = ComposeId(model.EntryId, model.Kind, model.LocalIndex);
        this.RequireSameOrEmptyId(id);

        Id = id;
        Version = model.Version;
        ChatId = model.EntryId.ChatId.Value;
        Kind = model.Kind;
        EntryId = model.EntryId.Value;
        EntryLocalId = model.EntryId.LocalId;
        LocalIndex = model.LocalIndex;
        At = model.At.ToDateTime();
        MediaId = model.MediaId?.Value;
        BlobId = model.BlobId;
        ThumbnailMediaId = model.ThumbnailMediaId?.Value;
        ThumbnailBlobId = model.ThumbnailBlobId;
        ContentType = model.ContentType;
        FileName = model.FileName;
        Size = model.Size;
        LinkPreviewId = model.LinkPreviewId.Value;
    }
}
