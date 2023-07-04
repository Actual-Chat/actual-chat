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
    public string MediaId { get; set; } = "";
    public int Index { get; set; }

    [Obsolete("Use MediaId")]
    public string ContentId { get; set; } = "";
    [Obsolete("Use MediaId")]
    public string MetadataJson { get; set; } = "";

    public static string ComposeId(TextEntryId entryId, int index)
        => $"{entryId}:{index}";

    public TextEntryAttachment ToModel()
        => new (Id, Version) {
            EntryId = new TextEntryId(EntryId),
            Index = Index,
            MediaId = new MediaId(MediaId),
        };

    public void UpdateFrom(TextEntryAttachment model)
    {
        var id = ComposeId(model.EntryId, model.Index);
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        EntryId = model.EntryId;
        Index = model.Index;
        MediaId = model.MediaId;
    }
}
