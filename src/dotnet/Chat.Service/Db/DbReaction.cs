using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Reactions")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbReaction : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string AuthorId { get; set; } = "";
    public string EntryId { get; set; } = "";
    [Column("emoji_id")] // TODO(AY): Rename to emoji
    public string Emoji { get; set; } = "";

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public static string ComposeId(TextEntryId entryId, AuthorId authorId)
        => $"{entryId}:{authorId}";

    public DbReaction() { }
    public DbReaction(Reaction model) => UpdateFrom(model);

    public Reaction ToModel()
        => new () {
            Id = Id,
            Version = Version,
            AuthorId = ActualChat.AuthorId.Parse(AuthorId),
            EntryId = TextEntryId.Parse(EntryId),
            Emoji = ActualChat.Emoji.Parse(Emoji),
            ModifiedAt = ModifiedAt,
        };

    public void UpdateFrom(Reaction model)
    {
        var id = ComposeId(model.EntryId, model.AuthorId);
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        AuthorId = model.AuthorId.Value;
        EntryId = model.EntryId.Value;
        Emoji = model.Emoji.Value;
        ModifiedAt = model.ModifiedAt;
    }
}
