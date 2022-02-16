using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("TextEntryAttachments")]
public class DbTextEntryAttachment
{
    public DbTextEntryAttachment() { }
    public DbTextEntryAttachment(TextEntryAttachment model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";
    public string ChatId { get; set; } = "";
    public long EntryId { get; set; }
    public int Index { get; set; }
    [ConcurrencyCheck] public long Version { get; set; }
    public string ContentId { get; set; } = "";
    public long Length { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";

    public TextEntryAttachment ToModel()
        => new () {
            Id = Id,
            ChatId = ChatId,
            EntryId = EntryId,
            Index = Index,
            Version = Version,

            ContentId = ContentId,
            ContentType = ContentType,
            FileName = FileName,
            Length = Length
        };

    private void UpdateFrom(TextEntryAttachment model)
    {
        ChatId = model.ChatId;
        EntryId = model.EntryId;
        Id = model.Id;
        Index = model.Index;
        Version = model.Version;
        ContentId = model.ContentId;
        Length = model.Length;
        FileName = model.FileName;
        ContentType = model.ContentType;
    }
}
