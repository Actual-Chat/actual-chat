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
        => Invariant($"{entryId.ChatId.Value}:{entryId.LocalId}:{index}");

    public TextEntryAttachment ToModel()
    {
        var entryId = new ChatEntryId(EntryId);
        return new () {
            Id = Id,
            Version = Version,
            EntryId = entryId,
            Index = Index,
            ContentId = ContentId,
            MetadataJson = MetadataJson
        };
    }

    public void UpdateFrom(TextEntryAttachment model)
    {
        var entryId = model.EntryId;
        if (entryId.EntryKind != ChatEntryKind.Text)
            throw new ArgumentOutOfRangeException(nameof(model), "Attachments are allowed only for text entries.");

        Id = ComposeId(entryId, model.Index);
        Version = model.Version;
        EntryId = entryId;
        Index = model.Index;
        ContentId = model.ContentId;
        MetadataJson = model.MetadataJson;
    }
}
