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
    public string MetadataJson { get; set; } = "";

    public static string ComposeId(string chatId, long entryId, int index)
        => Invariant($"{chatId}:{entryId}:{index}");

    public TextEntryAttachment ToModel()
        => new () {
            ChatId = ChatId,
            EntryId = EntryId,
            Index = Index,
            Version = Version,
            ContentId = ContentId,
            MetadataJson = MetadataJson
        };

    public void UpdateFrom(TextEntryAttachment model)
    {
        CompositeId = ComposeId(model.ChatId, model.EntryId, model.Index);
        ChatId = model.ChatId;
        EntryId = model.EntryId;
        Index = model.Index;
        Version = model.Version;
        ContentId = model.ContentId;
        MetadataJson = model.MetadataJson;
    }
}
