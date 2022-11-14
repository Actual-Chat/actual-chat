using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("Reactions")]
public class DbReaction : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _modifiedAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string AuthorId { get; set; } = "";
    public string EntryId { get; set; } = "";

    public string Emoji { get; set; } = "";

    public DateTime ModifiedAt {
        get => _modifiedAt.DefaultKind(DateTimeKind.Utc);
        set => _modifiedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public static string ComposeId(string chatEntryId, string authorId)
        => $"{chatEntryId}:{authorId}";

    public DbReaction() { }
    public DbReaction(Reaction model) => UpdateFrom(model);

    public Reaction ToModel()
    {
        var chatEntryId = new ChatEntryId(EntryId);
        return new () {
            Id = Id,
            Version = Version,
            AuthorId = AuthorId,
            EntryId = chatEntryId,
            Emoji = Emoji,
            ModifiedAt = ModifiedAt,
        };
    }

    public void UpdateFrom(Reaction model)
    {
        Id = ComposeId(model.EntryId, model.AuthorId);
        Version = model.Version;
        AuthorId = model.AuthorId;
        EntryId = model.EntryId;
        Emoji = model.Emoji;
        ModifiedAt = model.ModifiedAt;
    }
}
