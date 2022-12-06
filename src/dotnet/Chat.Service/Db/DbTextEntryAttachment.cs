using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("TextEntryAttachments")]
public class DbTextEntryAttachment : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public DbTextEntryAttachment() { }
    public DbTextEntryAttachment(TextEntryAttachment model) => UpdateFrom(model);

    // (ChatId, EntryId, Index)
    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string EntryId { get; set; } = "";
    public int Index { get; set; }

    public string ContentId { get; set; } = "";
    public string MetadataJson { get; set; } = "";

    public static string ComposeId(ChatEntryId entryId, int index)
    {
        if (entryId.EntryKind != ChatEntryKind.Text)
            throw new ArgumentOutOfRangeException(nameof(entryId), "Only text entries support attachments.");
        return Invariant($"{entryId.ChatId}:{entryId.LocalId}:{index}");
    }

    public TextEntryAttachment ToModel()
    {
        var entryId = new ChatEntryId(EntryId);
        return new (Id, Version) {
            EntryId = entryId,
            Index = Index,
            ContentId = ContentId,
            MetadataJson = MetadataJson
        };
    }

    public void UpdateFrom(TextEntryAttachment model)
    {
        var entryId = model.EntryId;
        var id = ComposeId(entryId, model.Index);
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        EntryId = entryId;
        Index = model.Index;
        ContentId = model.ContentId;
        MetadataJson = model.MetadataJson;
    }
}
