using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("TextEntryAttachments")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbTextEntryAttachment : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const char IdSeparator = ':';
    public DbTextEntryAttachment() { }
    public DbTextEntryAttachment(TextEntryAttachment model) => UpdateFrom(model);

    // (ChatId, EntryId, Index)
    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string EntryId { get; set; } = "";
    public string MediaId { get; set; } = "";
    public string ThumbnailMediaId { get; set; } = "";
    public int Index { get; set; }

    public static string ComposeId(ChatEntryId entryId, int index)
        => $"{entryId}{IdSeparator}{index}";

    public static string IdPrefix(ChatEntryId entryId)
        => entryId.Value + IdSeparator;

    public static ChatEntryId? ExtractTextEntryId(Symbol id)
    {
        var i = id.Value.LastIndexOf(IdSeparator);
        if (i < 0)
            return null;

        _ = ChatEntryId.TryParse(id.Value[..i], out var entryId);
        return entryId;
    }

    public TextEntryAttachment ToModel()
        => new (Id, Version) {
            EntryId = ChatEntryId.Parse(EntryId),
            Index = Index,
            MediaId = ActualChat.MediaId.Parse(MediaId),
            ThumbnailMediaId = ActualChat.MediaId.ParseNullable(ThumbnailMediaId),
        };

    public void UpdateFrom(TextEntryAttachment model)
    {
        var id = ComposeId(model.EntryId, model.Index);
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        EntryId = model.EntryId.Value;
        Index = model.Index;
        MediaId = model.MediaId.Value;
        ThumbnailMediaId = model.ThumbnailMediaId?.Value ?? "";
    }
}
