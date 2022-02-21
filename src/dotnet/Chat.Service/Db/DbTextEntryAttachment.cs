using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Chat.Db;

[Table("TextEntryAttachments")]
public class DbTextEntryAttachment
{
    public DbTextEntryAttachment() { }
    public DbTextEntryAttachment(TextEntryAttachment model) => UpdateFrom(model);

    // (ChatId, EntryId, Index)
    [Key] public string CompositeId { get; set; } = "";
    public string ChatId { get; set; } = "";
    public long EntryId { get; set; }
    public int Index { get; set; }
    [ConcurrencyCheck] public long Version { get; set; }
    public string ContentId { get; set; } = "";
    public long Length { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    public static string GetCompositeId(string chatId, long entryId, int index)
        => $"{chatId}:{entryId.ToString(CultureInfo.InvariantCulture)}:{index.ToString(CultureInfo.InvariantCulture)}";

    public TextEntryAttachment ToModel()
        => new () {
            ChatId = ChatId,
            EntryId = EntryId,
            Index = Index,
            Version = Version,

            ContentId = ContentId,
            ContentType = ContentType,
            FileName = FileName,
            Length = Length,
            Width = Width,
            Height = Height
        };

    public void UpdateFrom(TextEntryAttachment model)
    {
        CompositeId = GetCompositeId(model.ChatId, model.EntryId, model.Index);
        ChatId = model.ChatId;
        EntryId = model.EntryId;
        Index = model.Index;
        Version = model.Version;
        ContentId = model.ContentId;
        Length = model.Length;
        FileName = model.FileName;
        ContentType = model.ContentType;
        Width = model.Width;
        Height = model.Height;
    }
}
