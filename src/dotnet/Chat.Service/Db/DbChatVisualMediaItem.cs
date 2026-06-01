using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("ChatVisualMediaItems")]
[Index(nameof(ChatId), nameof(At), nameof(EntryLocalId), nameof(LocalIndex))]
[Index(nameof(EntryId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatVisualMediaItem : IHasId<string>, IHasVersion<long>, IRequirementTarget, IDbChatContentItem
{
    private const char IdSeparator = ':';

    public DbChatVisualMediaItem() { }
    public DbChatVisualMediaItem(VisualMediaItem model) => UpdateFrom(model);

    // (EntryId, LocalIndex)
    [DbKey] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public string EntryId { get; set; } = "";
    public long EntryLocalId { get; set; }
    public int LocalIndex { get; set; }

    public DateTime At {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public string MediaId { get; set; } = "";
    public string BlobId { get; set; } = "";
    public string? ThumbnailMediaId { get; set; }
    public string ThumbnailBlobId { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }

    public static string ComposeId(ChatEntryId entryId, int localIndex)
        => $"{entryId}{IdSeparator}{localIndex}";

    public VisualMediaItem ToModel()
        => new() {
            Id = Id,
            Version = Version,
            EntryId = ChatEntryId.Parse(EntryId),
            LocalIndex = LocalIndex,
            At = new Moment(At),
            MediaId = ActualChat.MediaId.Parse(MediaId),
            BlobId = BlobId,
            ThumbnailMediaId = ActualChat.MediaId.ParseNullable(ThumbnailMediaId),
            ThumbnailBlobId = ThumbnailBlobId,
            ContentType = ContentType,
            FileName = FileName,
            Size = Size,
        };

    public void UpdateFrom(VisualMediaItem model)
    {
        var id = ComposeId(model.EntryId, model.LocalIndex);
        this.RequireSameOrEmptyId(id);

        Id = id;
        Version = model.Version;
        ChatId = model.EntryId.ChatId.Value;
        EntryId = model.EntryId.Value;
        EntryLocalId = model.EntryId.LocalId;
        LocalIndex = model.LocalIndex;
        At = model.At.ToDateTime();
        MediaId = model.MediaId.Value;
        BlobId = model.BlobId;
        ThumbnailMediaId = model.ThumbnailMediaId?.Value;
        ThumbnailBlobId = model.ThumbnailBlobId;
        ContentType = model.ContentType;
        FileName = model.FileName;
        Size = model.Size;
    }
}
