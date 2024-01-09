using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Reactions")]
public class DbReaction : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _modifiedAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string AuthorId { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string EmojiId { get; set; } = "";

    public DateTime ModifiedAt {
        get => _modifiedAt.DefaultKind(DateTimeKind.Utc);
        set => _modifiedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public static string ComposeId(TextEntryId entryId, AuthorId authorId)
        => $"{entryId}:{authorId}";

    public DbReaction() { }
    public DbReaction(Reaction model) => UpdateFrom(model);

    public Reaction ToModel()
        => new () {
            Id = Id,
            Version = Version,
            AuthorId = new AuthorId(AuthorId),
            EntryId = new TextEntryId(EntryId),
            EmojiId = EmojiId,
            ModifiedAt = ModifiedAt,
        };

    public void UpdateFrom(Reaction model)
    {
        var id = ComposeId(model.EntryId, model.AuthorId);
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        AuthorId = model.AuthorId;
        EntryId = model.EntryId;
        EmojiId = model.EmojiId;
        ModifiedAt = model.ModifiedAt;
    }
}
