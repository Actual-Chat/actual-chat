using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("ChatFileItems")]
[Index(nameof(ChatId), nameof(At), nameof(EntryLocalId), nameof(LocalIndex))]
[Index(nameof(EntryId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatFileItem : IHasId<string>, IHasVersion<long>, IRequirementTarget, IDbChatContentItem
{
    private const char IdSeparator = ':';

    public DbChatFileItem() { }
    public DbChatFileItem(FileItem model) => UpdateFrom(model);

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
    public string ContentType { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }

    public static string ComposeId(ChatEntryId entryId, int localIndex)
        => $"{entryId}{IdSeparator}{localIndex}";

    public FileItem ToModel()
        => new() {
            Id = Id,
            Version = Version,
            EntryId = ChatEntryId.Parse(EntryId),
            LocalIndex = LocalIndex,
            At = new Moment(At),
            MediaId = ActualChat.MediaId.Parse(MediaId),
            BlobId = BlobId,
            ContentType = ContentType,
            FileName = FileName,
            Size = Size,
        };

    public void UpdateFrom(FileItem model)
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
        ContentType = model.ContentType;
        FileName = model.FileName;
        Size = model.Size;
    }
}
